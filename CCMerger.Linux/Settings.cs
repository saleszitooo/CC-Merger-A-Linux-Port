using System.Text.Json;

namespace CCMerger;

/// <summary>
/// Remembered settings. Replaces the App.config of the Windows version; lives in
/// ~/.config/CCMerger/settings.json (or $XDG_CONFIG_HOME/CCMerger/settings.json).
/// </summary>
internal sealed class AppSettings
{
    public string Folder { get; set; } = "";
    public int MaxPackageSizeMb { get; set; } = 100;
    public int MaxFilesPerPackage { get; set; } = 1000;
    public bool WriteContentLists { get; set; }

    private static string FilePath
    {
        get
        {
            string? config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(config))
                config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(config, "CCMerger", "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // broken settings file: just use the defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // not being able to save settings is not worth bothering anyone about
        }
    }

    /// <summary>
    /// The Windows version pre-fills "Documents/EA Games/The Sims 2 Ultimate Collection/Downloads".
    /// Here we try the same folder in the home directory and in the default Wine prefix.
    /// </summary>
    public static string FindDefaultFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
        if (string.IsNullOrWhiteSpace(winePrefix))
            winePrefix = Path.Combine(home, ".wine");

        string[] candidates =
        {
            Path.Combine(home, "Documents", "EA Games", "The Sims™ 2 Ultimate Collection", "Downloads"),
            Path.Combine(winePrefix, "drive_c", "users", Environment.UserName, "Documents", "EA Games", "The Sims™ 2 Ultimate Collection", "Downloads"),
        };

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }
        return "";
    }
}
