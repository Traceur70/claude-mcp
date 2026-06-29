namespace OneDriveMcp.Options;

/// <summary>
/// Les 3 valeurs que vous saisissez (+ tenant par defaut "consumers" pour un OneDrive perso).
/// Lues depuis la configuration : section "AzureAd" de appsettings.json OU App Settings Azure
/// (cles a plat : AzureAd__ClientId, AzureAd__ClientSecret, AzureAd__TenantId).
/// </summary>
public sealed class AzureAdOptions
{
    public const string SectionName = "AzureAd";

    /// <summary>Application (client) ID de l'inscription d'application Entra.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Secret client genere dans le portail Azure.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Tenant ID. Pour un OneDrive PERSO laissez "consumers".
    /// Pour un OneDrive Entreprise, mettez votre tenant id (GUID) ou "organizations".
    /// </summary>
    public string TenantId { get; set; } = "consumers";

    /// <summary>
    /// URI de redirection OAuth. Laissez vide : il est calcule automatiquement a partir
    /// de WEBSITE_HOSTNAME (Azure). Vous devez juste enregistrer cette URL dans l'app
    /// registration : https://&lt;votre-app&gt;.azurewebsites.net/signin-oidc
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// Scopes Microsoft Graph demandes (delegues).
    /// Files.ReadWrite couvre la lecture ET l'ecriture (upload). Si vous reduisez a
    /// Files.Read, l'upload echouera et il faudra retirer l'outil upload_file.
    /// </summary>
    public string[] Scopes { get; set; } =
    {
        "https://graph.microsoft.com/Files.ReadWrite",
        "https://graph.microsoft.com/User.Read"
    };
}
