using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using OneDriveMcp.Files;
using OneDriveMcp.Graph;
using OneDriveMcp.Options;

namespace OneDriveMcp.Mcp;

/// <summary>
/// Outils MCP exposes a Claude pour explorer et lire les Excel/CSV du OneDrive.
/// </summary>
[McpServerToolType]
public sealed class OneDriveTools
{
    private readonly OneDriveClient _oneDrive;
    private readonly SpreadsheetReader _reader;
    private readonly McpOptions _options;

    public OneDriveTools(OneDriveClient oneDrive, SpreadsheetReader reader, IOptions<McpOptions> options)
    {
        _oneDrive = oneDrive;
        _reader = reader;
        _options = options.Value;
    }

    [McpServerTool(Name = "list_files")]
    [Description("Liste les fichiers Excel (.xlsx/.xls) et CSV du OneDrive. " +
                 "Sans dossier, liste la racine. Indiquez un chemin de dossier pour naviguer (ex: 'Documents/Compta').")]
    public async Task<object> ListFiles(
        [Description("Chemin du dossier OneDrive a lister, relatif a la racine. Vide = racine.")] string? folder = null,
        [Description("Inclure aussi les dossiers (pour naviguer). Defaut: true.")] bool includeFolders = true,
        [Description("Nombre maximum d'elements. Defaut: 100.")] int top = 100)
    {
        try
        {
            var items = await _oneDrive.ListAsync(folder, Math.Clamp(top, 1, 999), onlySpreadsheets: false);
            var filtered = items.Where(i => includeFolders || !i.IsFolder)
                                .Where(i => i.IsFolder || IsSpreadsheet(i.Name));
            return new
            {
                folder = string.IsNullOrWhiteSpace(folder) ? "(racine)" : folder,
                items = filtered.Select(i => new { i.Id, i.Name, type = i.IsFolder ? "folder" : "file", i.Size, i.Path }).ToList()
            };
        }
        catch (NotConnectedException ex) { return Error(ex.Message); }
        catch (Exception ex) { return Error(ex.Message); }
    }

    [McpServerTool(Name = "search_files")]
    [Description("Recherche des fichiers Excel/CSV par nom dans tout le OneDrive.")]
    public async Task<object> SearchFiles(
        [Description("Terme de recherche (nom de fichier ou fragment).")] string query,
        [Description("Nombre maximum de resultats. Defaut: 50.")] int top = 50)
    {
        try
        {
            var items = await _oneDrive.SearchAsync(query, Math.Clamp(top, 1, 999), onlySpreadsheets: true);
            return new
            {
                query,
                results = items.Where(i => !i.IsFolder)
                               .Select(i => new { i.Id, i.Name, i.Size, i.Path, i.WebUrl }).ToList()
            };
        }
        catch (NotConnectedException ex) { return Error(ex.Message); }
        catch (Exception ex) { return Error(ex.Message); }
    }

    [McpServerTool(Name = "read_spreadsheet")]
    [Description("Lit le contenu d'un fichier Excel ou CSV et renvoie les lignes/colonnes. " +
                 "Identifiez le fichier par son 'id' (renvoye par list_files/search_files) ou par son chemin (ex: 'Documents/ventes.xlsx').")]
    public async Task<object> ReadSpreadsheet(
        [Description("Id du fichier OU chemin relatif a la racine du OneDrive.")] string file,
        [Description("Nom de la feuille a lire (Excel uniquement). Vide = premiere feuille.")] string? sheet = null,
        [Description("Nombre maximum de lignes de donnees a renvoyer. Defaut: configuration serveur.")] int? maxRows = null)
    {
        try
        {
            var (name, bytes) = await _oneDrive.DownloadFileAsync(file);
            var limit = Math.Clamp(maxRows ?? _options.DefaultMaxRows, 1, 50_000);

            var data = _reader.Read(name, bytes, sheet, limit);
            return data;
        }
        catch (NotConnectedException ex) { return Error(ex.Message); }
        catch (Exception ex) { return Error(ex.Message); }
    }

    // Limite de taille du contenu accepte en entree (le base64 transite dans le message MCP).
    private const int MaxUploadBytes = 25 * 1024 * 1024; // 25 Mo

    [McpServerTool(Name = "upload_file", Destructive = true, Title = "Televerser un fichier sur OneDrive")]
    [Description("Televerse un fichier sur OneDrive au chemin indique (ex: 'Documents/rapport.txt'). " +
                 "Fournissez SOIT 'content' (texte UTF-8) SOIT 'contentBase64' (binaire encode en base64). " +
                 "Par securite, si un fichier existe deja au meme chemin, l'outil REFUSE et renvoie " +
                 "status='confirmation_required' : demandez confirmation a l'utilisateur, puis rappelez " +
                 "l'outil avec overwrite=true pour l'ecraser.")]
    public async Task<object> UploadFile(
        [Description("Chemin de destination, relatif a la racine du OneDrive, incluant le nom du fichier (ex: 'Documents/notes.txt'). Le dossier parent doit exister.")] string path,
        [Description("Contenu texte (UTF-8) du fichier. A utiliser pour les fichiers texte.")] string? content = null,
        [Description("Contenu binaire encode en base64. A utiliser pour les fichiers non-texte (images, xlsx, pdf...).")] string? contentBase64 = null,
        [Description("Ecraser le fichier s'il existe deja. Defaut: false. Ne passez true qu'apres confirmation explicite de l'utilisateur.")] bool overwrite = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return Error("Le chemin de destination est requis.");

            byte[] bytes;
            if (!string.IsNullOrEmpty(contentBase64))
            {
                try { bytes = Convert.FromBase64String(contentBase64); }
                catch (FormatException) { return Error("contentBase64 n'est pas une chaine base64 valide."); }
            }
            else if (content is not null)
            {
                bytes = System.Text.Encoding.UTF8.GetBytes(content);
            }
            else
            {
                return Error("Fournissez 'content' (texte) ou 'contentBase64' (binaire).");
            }

            if (bytes.LongLength > MaxUploadBytes)
                return Error($"Fichier trop volumineux ({bytes.LongLength} octets). Limite: {MaxUploadBytes} octets (25 Mo).");

            // Garde-fou cote serveur : pas d'ecrasement sans confirmation explicite.
            if (!overwrite && await _oneDrive.ItemExistsAsync(path))
            {
                return new
                {
                    status = "confirmation_required",
                    path,
                    message = $"Un fichier existe deja a '{path}'. Demandez confirmation a l'utilisateur, " +
                              "puis rappelez upload_file avec overwrite=true pour l'ecraser."
                };
            }

            var result = await _oneDrive.UploadFileAsync(path, bytes, overwrite);
            return new
            {
                status = "uploaded",
                result.Name,
                result.Id,
                result.Size,
                result.WebUrl,
                overwritten = overwrite
            };
        }
        catch (FileExistsException)
        {
            return new
            {
                status = "confirmation_required",
                path,
                message = $"Un fichier existe deja a '{path}'. Rappelez upload_file avec overwrite=true pour l'ecraser."
            };
        }
        catch (NotConnectedException ex) { return Error(ex.Message); }
        catch (Exception ex) { return Error(ex.Message); }
    }

    private static bool IsSpreadsheet(string name) =>
        name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    private static object Error(string message) => new { error = message };
}
