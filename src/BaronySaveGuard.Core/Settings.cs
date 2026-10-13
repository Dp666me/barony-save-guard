using System.Text.Json;

namespace BaronySaveGuard;

/// <summary>User settings, stored as JSON next to the backups.</summary>
public sealed class Settings
{
    /// <summary>Save folder to watch. Empty means "find it automatically".</summary>
    public string SaveFolder { get; set; } = "";

    /// <summary>Where backups are kept. Empty means the default app data folder.</summary>
    public string BackupFolder { get; set; } = "";

    /// <summary>Put a save back automatically when the game deletes it.</summary>
    public bool AutoRestore { get; set; } = true;

    /// <summary>How many versions to keep per save file.</summary>
    public int MaxVersionsPerSave { get; set; } = 30;

    public static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "BaronySaveGuard");

    public static string DefaultBackupFolder => Path.Combine(AppDataFolder, "backups");

    public static string SettingsPath => Path.Combine(AppDataFolder, "settings.json");

    public string EffectiveBackupFolder =>
        string.IsNullOrWhiteSpace(BackupFolder) ? DefaultBackupFolder : BackupFolder;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load(string? path = null)
    {
        path ??= SettingsPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch (Exception)
        {
            // A broken settings file should never stop the backups; fall back to defaults.
        }
        return new Settings();
    }

    public void Save(string? path = null)
    {
        path ??= SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
