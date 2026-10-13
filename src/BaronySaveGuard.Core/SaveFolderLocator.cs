using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BaronySaveGuard;

/// <summary>Finds Barony's "savegames" folder.</summary>
public static partial class SaveFolderLocator
{
    /// <summary>
    /// Best guess at the save folder: the running game's folder first, then known install
    /// locations, preferring folders that already hold saves. Null if nothing was found.
    /// </summary>
    public static string? Find()
    {
        var fromProcess = FromRunningGame();
        if (fromProcess != null) return fromProcess;

        var existing = Candidates().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return existing.FirstOrDefault(d => Directory.EnumerateFiles(d, "*" + SaveFiles.Extension).Any())
               ?? existing.FirstOrDefault();
    }

    public static bool IsGameRunning() => GameProcesses().Any();

    public static string? FromRunningGame()
    {
        foreach (var process in GameProcesses())
        {
            try
            {
                var exe = process.MainModule?.FileName;
                if (exe == null) continue;
                var folder = Path.Combine(Path.GetDirectoryName(exe)!, "savegames");
                if (Directory.Exists(folder)) return folder;
            }
            catch (Exception)
            {
                // Access denied or the process just exited; try the next one.
            }
        }
        return null;
    }

    private static IEnumerable<Process> GameProcesses() =>
        new[] { "barony", "barony.x86_64", "Barony" }
            .SelectMany(n => { try { return Process.GetProcessesByName(n); } catch { return Array.Empty<Process>(); } });

    public static IEnumerable<string> Candidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var gameDirs = new List<string>();

        foreach (var library in SteamLibraries(home))
            gameDirs.Add(Path.Combine(library, "steamapps", "common", "Barony"));

        if (OperatingSystem.IsWindows())
        {
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            gameDirs.Add(Path.Combine(pf, "Epic Games", "Barony"));
            gameDirs.Add(@"C:\GOG Games\Barony");
            gameDirs.Add(Path.Combine(pf86, "GOG Galaxy", "Games", "Barony"));
            gameDirs.Add(Path.Combine(pf86, "GOG.com", "Barony"));
        }
        else
        {
            gameDirs.Add(Path.Combine(home, ".barony"));
            gameDirs.Add(Path.Combine(home, "GOG Games", "Barony", "game"));
        }

        return gameDirs.Select(d => Path.Combine(d, "savegames"));
    }

    private static IEnumerable<string> SteamLibraries(string home)
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        }
        else
        {
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }

        var libraries = new List<string>(roots);
        foreach (var root in roots)
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                foreach (Match m in LibraryPathRegex().Matches(File.ReadAllText(vdf)))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathRegex();
}
