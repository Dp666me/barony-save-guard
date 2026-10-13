namespace BaronySaveGuard.Tests;

public sealed class GameLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bsg-launch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string MakeFile(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void FindsSaveFolderNextToGame()
    {
        var game = MakeFile("Barony", "barony.exe");
        Directory.CreateDirectory(Path.Combine(_root, "Barony", "savegames"));
        Assert.Equal(Path.Combine(_root, "Barony", "savegames"), GameLauncher.SaveFolderFromCommand(new[] { game, "-windowed" }));
    }

    [Fact]
    public void LooksPastSteamWrappers()
    {
        var wrapper = MakeFile("steam", "reaper");
        var game = MakeFile("Barony", "barony.x86_64");
        Directory.CreateDirectory(Path.Combine(_root, "Barony", "savegames"));
        Assert.Equal(Path.Combine(_root, "Barony", "savegames"),
            GameLauncher.SaveFolderFromCommand(new[] { wrapper, "SteamLaunch", "--", game }));
    }

    [Fact]
    public void OwnOptionsAreNotALaunchCommand()
    {
        Assert.False(GameLauncher.IsLaunchCommand(new[] { "list" }));
        Assert.False(GameLauncher.IsLaunchCommand(new[] { "--folder", "x" }));
        Assert.False(GameLauncher.IsLaunchCommand(Array.Empty<string>()));
        Assert.True(GameLauncher.IsLaunchCommand(new[] { MakeFile("Barony", "barony.exe") }));
    }
}
