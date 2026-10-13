using System.Diagnostics;

namespace BaronySaveGuard;

/// <summary>
/// Steam launch-option support: Steam passes the game's command line in place of %command%,
/// e.g. <c>"C:\...\BaronySaveGuard.exe" %command%</c>. We start that command and guard the saves.
/// </summary>
public sealed class GameLauncher
{
    /// <summary>The process we started (may be a wrapper on Linux, not the game itself).</summary>
    public Process Process { get; }

    /// <summary>The "savegames" folder next to the launched game, if one could be found.</summary>
    public string? SaveFolder { get; }

    private GameLauncher(Process process, string? saveFolder)
    {
        Process = process;
        SaveFolder = saveFolder;
    }

    /// <summary>True when the arguments look like a game command line rather than our own options.</summary>
    public static bool IsLaunchCommand(string[] args) => args.Length > 0 && File.Exists(args[0]);

    public static GameLauncher Start(string[] command)
    {
        var start = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(command[0]))!,
        };
        foreach (var arg in command.Skip(1)) start.ArgumentList.Add(arg);
        var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {command[0]}");
        return new GameLauncher(process, SaveFolderFromCommand(command));
    }

    /// <summary>
    /// Finds "savegames" next to any file named in the command. On Linux, Steam's %command% can be a
    /// chain of wrappers before the game, so every argument is checked, not only the first.
    /// </summary>
    public static string? SaveFolderFromCommand(IEnumerable<string> command)
    {
        foreach (var arg in command)
        {
            try
            {
                if (!File.Exists(arg)) continue;
                var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(arg))!, "savegames");
                if (Directory.Exists(folder)) return folder;
            }
            catch (Exception) { }
        }
        return null;
    }

    /// <summary>
    /// True while the game is still running: the process we started, or (if that was a launcher
    /// or wrapper that handed off) any Barony process.
    /// </summary>
    public bool GameStillRunning()
    {
        try { if (!Process.HasExited) return true; } catch (InvalidOperationException) { }
        return SaveFolderLocator.IsGameRunning();
    }
}
