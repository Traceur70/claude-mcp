using Microsoft.AspNetCore.DataProtection;
using OneDriveMcp.Auth;
using OneDriveMcp.Files;
using OneDriveMcp.Graph;
using OneDriveMcp.Mcp;
using OneDriveMcp.Options;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration (les 3 valeurs + cle API) ---
builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection(AzureAdOptions.SectionName));
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName));

// --- Data Protection : cles persistees sur le disque persistant Azure (chiffre le cache MSAL) ---
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Storage.KeysDirectory))
    .SetApplicationName("OneDriveMcp");

builder.Services.AddHttpClient();

// --- Services applicatifs ---
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddScoped<OneDriveClient>();
builder.Services.AddSingleton<SpreadsheetReader>();

// --- Serveur MCP (transport HTTP / Streamable) ---
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<OneDriveTools>();

var app = builder.Build();

// --- Cle API appliquee uniquement aux requetes /mcp ---
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseMiddleware<ApiKeyMiddleware>());

// --- Endpoint MCP (transport HTTP / Streamable) sous /mcp ---
app.MapMcp("/mcp");

// --- Page d'accueil : statut + lien de connexion ---
app.MapGet("/", async (TokenStore tokens) =>
{
    var connected = await tokens.IsConnectedAsync();
    var account = connected ? await tokens.GetConnectedAccountAsync() : null;
    var host = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME") ?? "localhost";

    var statusHtml = connected
        ? $"<p style='color:green'><b>Connecte</b> en tant que {account}. Le serveur MCP est operationnel.</p>"
        : "<p style='color:#c00'><b>Non connecte.</b> Cliquez ci-dessous pour autoriser l'acces a votre OneDrive (une seule fois).</p>";

    var html = $$"""
        <!doctype html><html lang="fr"><head><meta charset="utf-8">
        <title>OneDrive MCP</title>
        <style>body{font-family:system-ui;max-width:640px;margin:48px auto;padding:0 16px;line-height:1.5}
        a.btn{display:inline-block;padding:10px 18px;background:#0067b8;color:#fff;text-decoration:none;border-radius:6px}
        code{background:#f3f3f3;padding:2px 6px;border-radius:4px}</style></head>
        <body>
        <h1>Serveur MCP OneDrive</h1>
        {{statusHtml}}
        <p><a class="btn" href="/login">Se connecter a Microsoft / OneDrive</a></p>
        <h3>URL du connecteur MCP (a donner a Claude)</h3>
        <p><code>https://{{host}}/mcp</code></p>
        <p>Ajoutez la cle API via le header <code>X-API-Key</code> ou en suffixe d'URL <code>?api_key=...</code></p>
        </body></html>
        """;

    return Results.Content(html, "text/html");
});

// --- Demarre la connexion Microsoft ---
app.MapGet("/login", async (TokenStore tokens) =>
{
    var url = await tokens.GetAuthorizationRequestUrlAsync();
    return Results.Redirect(url.ToString());
});

// --- Callback OAuth : echange le code et persiste le refresh token ---
app.MapGet("/signin-oidc", async (HttpRequest request, TokenStore tokens) =>
{
    var error = request.Query["error"].FirstOrDefault();
    if (!string.IsNullOrEmpty(error))
    {
        var desc = request.Query["error_description"].FirstOrDefault();
        return Results.Content($"<h1>Echec de connexion</h1><p>{error} : {desc}</p>", "text/html");
    }

    var code = request.Query["code"].FirstOrDefault();
    if (string.IsNullOrEmpty(code))
        return Results.Content("<h1>Code d'autorisation manquant.</h1>", "text/html");

    await tokens.RedeemCodeAsync(code);
    return Results.Content(
        "<h1 style='color:green'>Connecte a OneDrive !</h1><p>Vous pouvez fermer cet onglet. Le serveur MCP est pret.</p>",
        "text/html");
});

// --- Sonde de sante ---
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
