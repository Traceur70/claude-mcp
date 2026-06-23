namespace OneDriveMcp.Options;

/// <summary>
/// Protection de l'acces au serveur MCP (public sur Internet).
/// Section "Mcp" / cle Azure : Mcp__ApiKey
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>
    /// Cle API attendue. Claude doit l'envoyer via le header "X-API-Key"
    /// ou via le parametre d'URL "?api_key=...". Si vide, l'acces MCP est ouvert
    /// (deconseille en production).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Nombre maximum de lignes renvoyees par defaut lors de la lecture d'un fichier.</summary>
    public int DefaultMaxRows { get; set; } = 500;
}
