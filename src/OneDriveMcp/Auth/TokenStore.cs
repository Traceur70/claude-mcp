using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using OneDriveMcp.Options;

namespace OneDriveMcp.Auth;

/// <summary>
/// Gere l'authentification deleguee vers Microsoft (compte perso) via MSAL.
///
/// Flux :
///  1. /login  -> GetAuthorizationRequestUrlAsync() : on envoie l'utilisateur se connecter.
///  2. /signin-oidc?code=... -> RedeemCodeAsync() : on echange le code, MSAL stocke un refresh token.
///  3. Ensuite, GetAccessTokenAsync() rafraichit silencieusement le jeton a la demande.
///
/// Le cache de jetons (donc le refresh token) est serialise sur disque dans le dossier
/// persistant d'Azure App Service (/home/data), chiffre via ASP.NET Data Protection.
/// </summary>
public sealed class TokenStore
{
    private readonly IConfidentialClientApplication _app;
    private readonly string[] _scopes;
    private readonly string _cacheFile;
    private readonly IDataProtector _protector;
    private readonly object _fileLock = new();

    public TokenStore(IOptions<AzureAdOptions> options, IDataProtectionProvider dataProtection)
    {
        var o = options.Value;
        _scopes = o.Scopes;

        var authority = $"https://login.microsoftonline.com/{o.TenantId}";

        _app = ConfidentialClientApplicationBuilder
            .Create(o.ClientId)
            .WithClientSecret(o.ClientSecret)
            .WithAuthority(authority)
            .WithRedirectUri(ResolveRedirectUri(o))
            .Build();

        _protector = dataProtection.CreateProtector("OneDriveMcp.MsalTokenCache");
        _cacheFile = Path.Combine(Storage.DataDirectory, "msal_cache.bin");

        _app.UserTokenCache.SetBeforeAccess(BeforeAccess);
        _app.UserTokenCache.SetAfterAccess(AfterAccess);
    }

    /// <summary>Calcule l'URI de redirection : valeur configuree, sinon WEBSITE_HOSTNAME (Azure), sinon localhost.</summary>
    public static string ResolveRedirectUri(AzureAdOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.RedirectUri))
            return o.RedirectUri!;

        var host = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
        if (!string.IsNullOrEmpty(host))
            return $"https://{host}/signin-oidc";

        return "https://localhost:5001/signin-oidc";
    }

    /// <summary>True si un compte est deja connecte (refresh token present).</summary>
    public async Task<bool> IsConnectedAsync()
    {
        var accounts = await _app.GetAccountsAsync();
        return accounts.Any();
    }

    /// <summary>Nom/upn du compte connecte, ou null.</summary>
    public async Task<string?> GetConnectedAccountAsync()
    {
        var accounts = await _app.GetAccountsAsync();
        return accounts.FirstOrDefault()?.Username;
    }

    /// <summary>URL de connexion Microsoft a ouvrir dans le navigateur.</summary>
    public async Task<Uri> GetAuthorizationRequestUrlAsync()
    {
        return await _app.GetAuthorizationRequestUrl(_scopes).ExecuteAsync();
    }

    /// <summary>Echange le code d'autorisation recu sur /signin-oidc et persiste le cache.</summary>
    public async Task RedeemCodeAsync(string code)
    {
        await _app.AcquireTokenByAuthorizationCode(_scopes, code).ExecuteAsync();
    }

    /// <summary>Retourne un jeton d'acces Graph valide, ou null si non connecte.</summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        var accounts = await _app.GetAccountsAsync();
        var account = accounts.FirstOrDefault();
        if (account is null)
            return null;

        var result = await _app.AcquireTokenSilent(_scopes, account).ExecuteAsync();
        return result.AccessToken;
    }

    private void BeforeAccess(TokenCacheNotificationArgs args)
    {
        lock (_fileLock)
        {
            if (!File.Exists(_cacheFile))
                return;

            try
            {
                var encrypted = File.ReadAllBytes(_cacheFile);
                var data = _protector.Unprotect(encrypted);
                args.TokenCache.DeserializeMsalV3(data);
            }
            catch
            {
                // Cache illisible (cle de protection changee, fichier corrompu) : on repart a zero.
            }
        }
    }

    private void AfterAccess(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged)
            return;

        lock (_fileLock)
        {
            var data = args.TokenCache.SerializeMsalV3();
            var encrypted = _protector.Protect(data);
            File.WriteAllBytes(_cacheFile, encrypted);
        }
    }
}
