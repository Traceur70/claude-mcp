using System.Net.Http.Headers;
using System.Text.Json;
using OneDriveMcp.Auth;

namespace OneDriveMcp.Graph;

public sealed record DriveItemInfo(
    string Id,
    string Name,
    bool IsFolder,
    long Size,
    string? Path,
    string? WebUrl);

/// <summary>
/// Acces a OneDrive via l'API REST Microsoft Graph (v1.0), avec un jeton delegue
/// fourni par <see cref="TokenStore"/>. On reste sur du REST pour eviter les soucis
/// de versions du SDK Graph et garder le code lisible.
/// </summary>
public sealed class OneDriveClient
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private static readonly string[] SpreadsheetExtensions = { ".xlsx", ".xls", ".csv" };

    private readonly IHttpClientFactory _httpFactory;
    private readonly TokenStore _tokenStore;

    public OneDriveClient(IHttpClientFactory httpFactory, TokenStore tokenStore)
    {
        _httpFactory = httpFactory;
        _tokenStore = tokenStore;
    }

    private async Task<HttpClient> CreateGraphClientAsync()
    {
        var token = await _tokenStore.GetAccessTokenAsync()
            ?? throw new NotConnectedException();

        var client = _httpFactory.CreateClient("graph");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Liste les fichiers/dossiers d'un dossier OneDrive (racine par defaut).</summary>
    public async Task<IReadOnlyList<DriveItemInfo>> ListAsync(string? folderPath, int top, bool onlySpreadsheets)
    {
        using var client = await CreateGraphClientAsync();

        string url = string.IsNullOrWhiteSpace(folderPath)
            ? $"{GraphBase}/me/drive/root/children"
            : $"{GraphBase}/me/drive/root:/{Uri.EscapeDataString(folderPath.Trim('/'))}:/children";

        url += $"?$top={top}&$select=id,name,size,folder,file,webUrl,parentReference";

        return await ReadItemsAsync(client, url, onlySpreadsheets);
    }

    /// <summary>Recherche des fichiers par nom dans tout le OneDrive.</summary>
    public async Task<IReadOnlyList<DriveItemInfo>> SearchAsync(string query, int top, bool onlySpreadsheets)
    {
        using var client = await CreateGraphClientAsync();
        var url = $"{GraphBase}/me/drive/root/search(q='{Uri.EscapeDataString(query)}')" +
                  $"?$top={top}&$select=id,name,size,folder,file,webUrl,parentReference";

        return await ReadItemsAsync(client, url, onlySpreadsheets);
    }

    /// <summary>Recupere le nom + l'URL de telechargement pre-authentifiee d'un fichier (par id ou chemin).</summary>
    public async Task<(string Name, string DownloadUrl)> GetDownloadInfoAsync(string idOrPath)
    {
        using var client = await CreateGraphClientAsync();

        bool looksLikePath = idOrPath.Contains('/') || idOrPath.Contains('.');
        string url = looksLikePath
            ? $"{GraphBase}/me/drive/root:/{Uri.EscapeDataString(idOrPath.Trim('/'))}"
            : $"{GraphBase}/me/drive/items/{Uri.EscapeDataString(idOrPath)}";

        url += "?$select=id,name,@microsoft.graph.downloadUrl";

        using var resp = await client.GetAsync(url);
        await EnsureSuccessAsync(resp);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        var name = root.GetProperty("name").GetString() ?? idOrPath;

        if (!root.TryGetProperty("@microsoft.graph.downloadUrl", out var dl) || dl.GetString() is not string downloadUrl)
            throw new InvalidOperationException($"Aucune URL de telechargement pour '{name}' (est-ce bien un fichier ?).");

        return (name, downloadUrl);
    }

    /// <summary>Telecharge le contenu brut depuis une URL de telechargement pre-authentifiee (sans bearer).</summary>
    public async Task<byte[]> DownloadAsync(string downloadUrl)
    {
        var client = _httpFactory.CreateClient(); // pas d'Authorization : l'URL est deja signee
        using var resp = await client.GetAsync(downloadUrl);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadAsByteArrayAsync();
    }

    private static async Task<IReadOnlyList<DriveItemInfo>> ReadItemsAsync(HttpClient client, string url, bool onlySpreadsheets)
    {
        using var resp = await client.GetAsync(url);
        await EnsureSuccessAsync(resp);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync());
        var items = new List<DriveItemInfo>();

        if (!doc.RootElement.TryGetProperty("value", out var values))
            return items;

        foreach (var el in values.EnumerateArray())
        {
            var name = el.GetProperty("name").GetString() ?? "";
            var isFolder = el.TryGetProperty("folder", out _);

            if (onlySpreadsheets && !isFolder && !IsSpreadsheet(name))
                continue;

            string? path = null;
            if (el.TryGetProperty("parentReference", out var parent) &&
                parent.TryGetProperty("path", out var p) && p.GetString() is string pp)
            {
                // pp ressemble a "/drive/root:/Dossier" -> on garde la partie apres "root:"
                var idx = pp.IndexOf("root:", StringComparison.Ordinal);
                var prefix = idx >= 0 ? pp[(idx + "root:".Length)..].Trim('/') : "";
                path = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
            }

            items.Add(new DriveItemInfo(
                el.GetProperty("id").GetString() ?? "",
                name,
                isFolder,
                el.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                path,
                el.TryGetProperty("webUrl", out var w) ? w.GetString() : null));
        }

        return items;
    }

    private static bool IsSpreadsheet(string name) =>
        SpreadsheetExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
            return;

        var body = await resp.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Erreur Graph {(int)resp.StatusCode} : {body}");
    }
}

/// <summary>Levee quand aucun compte n'est connecte.</summary>
public sealed class NotConnectedException : Exception
{
    public NotConnectedException()
        : base("Non connecte a OneDrive. Ouvrez l'URL /login de l'application dans un navigateur pour vous authentifier une fois.")
    {
    }
}
