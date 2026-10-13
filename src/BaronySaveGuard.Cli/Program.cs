using BaronySaveGuard;

// Console version for Linux / Steam Deck, or for anyone who prefers a terminal.
//   barony-save-guard [watch] [--folder <path>] [--no-restore]
//   barony-save-guard list
//   barony-save-guard restore <save name> [version number from "list"]
//   barony-save-guard %command%        (Steam launch option: starts the game, stops when it exits)

var settings = Settings.Load();
var command = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "watch";
var folderArg = OptionValue("--folder");
var store = new BackupStore(settings.EffectiveBackupFolder, settings.MaxVersionsPerSave);
if (GameLauncher.IsLaunchCommand(args)) return LaunchAndWatch();

switch (command)
{
    case "watch":
        return Watch();
    case "list":
        return List();
    case "restore":
        return Restore();
    default:
        Console.WriteLine("Usage: barony-save-guard [watch|list|restore <save> [version]] [--folder <path>] [--no-restore]");
        return 1;
}

int Watch()
{
    var folder = ResolveFolder();
    if (folder == null) return 1;

    using var guard = new SaveGuard(folder, store) { AutoRestore = settings.AutoRestore && !args.Contains("--no-restore") };
    guard.Event += e => Console.WriteLine(e);
    guard.Start();
    Console.WriteLine(guard.AutoRestore
        ? "Deleted saves will be put back automatically. Press Ctrl+C to stop."
        : "Auto-restore is off; saves are backed up only. Press Ctrl+C to stop.");

    var done = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
    done.Wait();
    return 0;
}

int LaunchAndWatch()
{
    var game = GameLauncher.Start(args);
    var folder = game.SaveFolder ?? ResolveFolder();
    if (folder == null)
    {
        game.Process.WaitForExit();
        return 1;
    }

    using var guard = new SaveGuard(folder, store) { AutoRestore = settings.AutoRestore };
    guard.Event += e => Console.WriteLine(e);
    guard.Start();

    // Keep guarding until the game is gone, plus a few seconds for a last-second deletion.
    var exitedAt = (DateTime?)null;
    while (exitedAt == null || DateTime.UtcNow - exitedAt < TimeSpan.FromSeconds(5))
    {
        Thread.Sleep(1000);
        if (game.GameStillRunning()) exitedAt = null;
        else exitedAt ??= DateTime.UtcNow;
    }
    Console.WriteLine("Game closed.");
    return 0;
}

int List()
{
    var names = store.SaveNames();
    if (names.Count == 0)
    {
        Console.WriteLine($"No backups yet in {store.Root}");
        return 0;
    }
    foreach (var name in names)
    {
        Console.WriteLine(name);
        var versions = store.Versions(name);
        for (var i = 0; i < versions.Count; i++)
            Console.WriteLine($"  {i + 1,3}. {versions[i].CreatedLocal:yyyy-MM-dd HH:mm:ss}  {versions[i].Size / 1024.0:0.0} KB");
    }
    return 0;
}

int Restore()
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: barony-save-guard restore <save name> [version number]");
        return 1;
    }
    var versions = store.Versions(args[1]);
    var index = args.Length > 2 && int.TryParse(args[2], out var n) ? n - 1 : 0;
    if (index < 0 || index >= versions.Count)
    {
        Console.WriteLine($"No such backup. Run \"barony-save-guard list\" to see what is available.");
        return 1;
    }
    var folder = ResolveFolder();
    if (folder == null) return 1;
    var guard = new SaveGuard(folder, store);
    guard.Event += e => Console.WriteLine(e);
    guard.RestoreVersion(versions[index]);
    return 0;
}

string? ResolveFolder()
{
    var folder = folderArg ?? (string.IsNullOrWhiteSpace(settings.SaveFolder) ? SaveFolderLocator.Find() : settings.SaveFolder);
    if (folder == null)
    {
        Console.WriteLine("Could not find Barony's savegames folder. Start the game once, or pass --folder <path>.");
        return null;
    }
    return folder;
}

string? OptionValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
