using System.Globalization;

namespace BaronySaveGuard;

/// <summary>One stored copy of a save file.</summary>
public sealed record BackupVersion(string SaveName, string Path, DateTime CreatedUtc, long Size)
{
    public DateTime CreatedLocal => CreatedUtc.ToLocalTime();
}

/// <summary>
/// Versioned on-disk backups. Layout: &lt;root&gt;/&lt;save file name&gt;/&lt;yyyyMMdd-HHmmss-fff&gt;.baronysave
/// </summary>
public sealed class BackupStore
{
    private const string TimeFormat = "yyyyMMdd-HHmmss-fff";
    private readonly object _lock = new();

    public string Root { get; }
    public int MaxVersionsPerSave { get; set; }

    public BackupStore(string root, int maxVersionsPerSave)
    {
        Root = root;
        MaxVersionsPerSave = Math.Max(1, maxVersionsPerSave);
        Directory.CreateDirectory(Root);
    }

    public IReadOnlyList<string> SaveNames()
    {
        if (!Directory.Exists(Root)) return Array.Empty<string>();
        return Directory.GetDirectories(Root)
            .Where(d => Directory.EnumerateFiles(d, "*" + SaveFiles.Extension).Any())
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Versions of one save, newest first.</summary>
    public IReadOnlyList<BackupVersion> Versions(string saveName)
    {
        var dir = Path.Combine(Root, saveName);
        if (!Directory.Exists(dir)) return Array.Empty<BackupVersion>();
        var list = new List<BackupVersion>();
        foreach (var file in Directory.GetFiles(dir, "*" + SaveFiles.Extension))
        {
            var stamp = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(stamp, TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created))
                created = File.GetLastWriteTimeUtc(file);
            list.Add(new BackupVersion(saveName, file, created, new FileInfo(file).Length));
        }
        return list.OrderByDescending(v => v.CreatedUtc).ToList();
    }

    public BackupVersion? Latest(string saveName) => Versions(saveName).FirstOrDefault();

    /// <summary>
    /// Stores <paramref name="content"/> as a new version unless it matches the latest one.
    /// Returns the new version, or null when nothing changed.
    /// </summary>
    public BackupVersion? Add(string saveName, byte[] content)
    {
        lock (_lock)
        {
            var latest = Latest(saveName);
            if (latest != null && SameContent(latest.Path, content)) return null;

            var dir = Path.Combine(Root, saveName);
            Directory.CreateDirectory(dir);
            var now = DateTime.UtcNow;
            // Never let a newer backup sort before an older one, even if the clock jumps back.
            if (latest != null && now <= latest.CreatedUtc) now = latest.CreatedUtc.AddMilliseconds(1);
            var path = Path.Combine(dir, now.ToString(TimeFormat, CultureInfo.InvariantCulture) + SaveFiles.Extension);
            while (File.Exists(path))
            {
                now = now.AddMilliseconds(1);
                path = Path.Combine(dir, now.ToString(TimeFormat, CultureInfo.InvariantCulture) + SaveFiles.Extension);
            }

            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, content);
            File.Move(tmp, path);
            Prune(saveName);
            return new BackupVersion(saveName, path, now, content.Length);
        }
    }

    private void Prune(string saveName)
    {
        foreach (var old in Versions(saveName).Skip(MaxVersionsPerSave))
        {
            try { File.Delete(old.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool SameContent(string path, byte[] content)
    {
        try
        {
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(content);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
