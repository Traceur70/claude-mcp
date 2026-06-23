using Microsoft.Extensions.Options;
using OneDriveMcp.Options;

namespace OneDriveMcp.Auth;

/// <summary>
/// Verifie la cle API sur les requetes MCP. Accepte le header "X-API-Key"
/// ou le parametre d'URL "?api_key=" (pratique pour les connecteurs qui ne
/// permettent pas d'ajouter des headers personnalises).
/// Si aucune cle n'est configuree, l'acces est laisse ouvert.
/// </summary>
public sealed class ApiKeyMiddleware
{
    public const string HeaderName = "X-API-Key";
    public const string QueryName = "api_key";

    private readonly RequestDelegate _next;
    private readonly string? _expectedKey;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<McpOptions> options)
    {
        _next = next;
        _expectedKey = options.Value.ApiKey;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_expectedKey))
        {
            await _next(context);
            return;
        }

        var provided = context.Request.Headers[HeaderName].FirstOrDefault()
                       ?? context.Request.Query[QueryName].FirstOrDefault();

        if (!string.Equals(provided, _expectedKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("401 - Cle API invalide ou manquante.");
            return;
        }

        await _next(context);
    }
}
