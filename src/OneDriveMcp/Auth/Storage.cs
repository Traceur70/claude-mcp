namespace OneDriveMcp.Auth;

/// <summary>
/// Emplacement de stockage persistant.
/// Sur Azure App Service, la variable HOME pointe vers un disque persistant partage
/// (/home sous Linux, D:\home sous Windows) qui survit aux redemarrages.
/// </summary>
public static class Storage
{
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string KeysDirectory { get; } = EnsureDir(Path.Combine(DataDirectory, "keys"));

    private static string ResolveDataDirectory()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        var baseDir = !string.IsNullOrEmpty(home)
            ? Path.Combine(home, "data", "onedrive-mcp")
            : Path.Combine(AppContext.BaseDirectory, "data");
        return EnsureDir(baseDir);
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
