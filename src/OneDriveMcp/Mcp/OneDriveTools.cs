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
            var (name, downloadUrl) = await _oneDrive.GetDownloadInfoAsync(file);
            var bytes = await _oneDrive.DownloadAsync(downloadUrl);
            var limit = Math.Clamp(maxRows ?? _options.DefaultMaxRows, 1, 50_000);

            var data = _reader.Read(name, bytes, sheet, limit);
            return data;
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
