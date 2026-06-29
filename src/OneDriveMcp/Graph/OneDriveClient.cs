using System.Net;
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

public sealed record UploadResult(
    string Name,
    string Id,
    long Size,
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
            : $"{GraphBase}/me/drive/root:/{EncodePath(folderPath)}:/children";

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

    /// <summary>
    /// Telecharge le contenu d'un fichier (par id ou chemin) et renvoie (nom, octets).
    ///
    /// On NE depend PAS de l'annotation @microsoft.graph.downloadUrl : elle n'est pas
    /// renvoyee de facon fiable (et la $select-er la fait disparaitre). On resout d'abord
    /// l'item (id + nom), puis on lit directement l'endpoint /content de Graph, qui renvoie
    /// le flux d'octets via une redirection 302 vers une URL pre-authentifiee.
    /// </summary>
    public async Task<(string Name, byte[] Content)> DownloadFileAsync(string idOrPath)
    {
        using var client = await CreateGraphClientAsync();

        bool looksLikePath = idOrPath.Contains('/') || idOrPath.Contains('.');
        string itemUrl = looksLikePath
            ? $"{GraphBase}/me/drive/root:/{EncodePath(idOrPath)}"
            : $"{GraphBase}/me/drive/items/{Uri.EscapeDataString(idOrPath)}";

        using var resp = await client.GetAsync($"{itemUrl}?$select=id,name,file,folder");
        await EnsureSuccessAsync(resp);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetString()!;
        var name = root.GetProperty("name").GetString() ?? idOrPath;

        if (root.TryGetProperty("folder", out _))
            throw new InvalidOperationException($"'{name}' est un dossier, pas un fichier.");

        var bytes = await DownloadContentAsync(id);
        return (name, bytes);
    }

    /// <summary>
    /// Lit l'endpoint /content d'un item. Graph repond par un 302 vers une URL pre-signee :
    /// on suit la redirection manuellement et on telecharge SANS en-tete d'autorisation
    /// (sinon le stockage la rejette). Certains petits fichiers peuvent revenir en 200 direct.
    /// </summary>
    private async Task<byte[]> DownloadContentAsync(string itemId)
    {
        var token = await _tokenStore.GetAccessTokenAsync() ?? throw new NotConnectedException();

        // Client sans suivi automatique de redirection pour intercepter le 302.
        var noRedirect = _httpFactory.CreateClient("graph-content");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{GraphBase}/me/drive/items/{Uri.EscapeDataString(itemId)}/content");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await noRedirect.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

        if (resp.StatusCode is HttpStatusCode.Found or HttpStatusCode.Redirect
            or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect or HttpStatusCode.SeeOther)
        {
            var location = resp.Headers.Location
                ?? throw new InvalidOperationException("Redirection 302 sans en-tete Location pour le contenu du fichier.");

            var plain = _httpFactory.CreateClient(); // pas d'Authorization : l'URL est deja signee
            using var dl = await plain.GetAsync(location);
            await EnsureSuccessAsync(dl);
            return await dl.Content.ReadAsByteArrayAsync();
        }

        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Indique si un item (fichier ou dossier) existe au chemin donne.</summary>
    public async Task<bool> ItemExistsAsync(string path)
    {
        using var client = await CreateGraphClientAsync();
        using var resp = await client.GetAsync($"{GraphBase}/me/drive/root:/{EncodePath(path)}?$select=id");
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return false;
        await EnsureSuccessAsync(resp);
        return true;
    }

    /// <summary>
    /// Televerse un fichier au chemin donne (simple upload, jusqu'a 250 Mo).
    /// conflictBehavior=replace si overwrite, sinon fail (erreur si le fichier existe deja).
    /// Renvoie (nom, id, taille, webUrl) de l'item cree/mis a jour.
    /// </summary>
    public async Task<UploadResult> UploadFileAsync(string path, byte[] content, bool overwrite)
    {
        using var client = await CreateGraphClientAsync();

        var conflict = overwrite ? "replace" : "fail";
        var url = $"{GraphBase}/me/drive/root:/{EncodePath(path)}:/content?@microsoft.graph.conflictBehavior={conflict}";

        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var resp = await client.PutAsync(url, body);

        // 409 = le fichier existe et conflictBehavior=fail : on remonte un cas dedie.
        if (resp.StatusCode == HttpStatusCode.Conflict)
            throw new FileExistsException(path);

        await EnsureSuccessAsync(resp);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        return new UploadResult(
            root.GetProperty("name").GetString() ?? "",
            root.GetProperty("id").GetString() ?? "",
            root.TryGetProperty("size", out var s) ? s.GetInt64() : content.LongLength,
            root.TryGetProperty("webUrl", out var w) ? w.GetString() : null);
    }

    /// <summary>Encode chaque segment d'un chemin OneDrive en conservant les '/' separateurs.</summary>
    private static string EncodePath(string path) =>
        string.Join('/', path.Trim('/').Split('/').Select(Uri.EscapeDataString));

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

/// <summary>Levee quand un upload sans overwrite cible un fichier existant.</summary>
public sealed class FileExistsException : Exception
{
    public FileExistsException(string path)
        : base($"Le fichier '{path}' existe deja.")
    {
        Path = path;
    }

    public string Path { get; }
}
