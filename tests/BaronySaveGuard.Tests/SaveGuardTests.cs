namespace BaronySaveGuard.Tests;

public sealed class SaveGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bsg-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _saves;
    private readonly BackupStore _store;
    private readonly List<GuardEvent> _events = new();

    public SaveGuardTests()
    {
        _saves = Path.Combine(_root, "savegames");
        Directory.CreateDirectory(_saves);
        _store = new BackupStore(Path.Combine(_root, "backups"), 5);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private SaveGuard StartGuard(bool autoRestore = true)
    {
        var guard = new SaveGuard(_saves, _store)
        {
            AutoRestore = autoRestore,
            ChangeSettleDelay = TimeSpan.FromMilliseconds(100),
            RestoreDelay = TimeSpan.FromMilliseconds(400),
            SweepInterval = TimeSpan.FromMilliseconds(500),
        };
        guard.Event += e => { lock (_events) _events.Add(e); };
        guard.Start();
        return guard;
    }

    private string SavePath(string name) => Path.Combine(_saves, name);

    private static void WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Condition not met in time");
            Thread.Sleep(50);
        }
    }

    [Fact]
    public void BacksUpExistingSavesOnStart()
    {
        File.WriteAllBytes(SavePath("savegame0.baronysave"), new byte[] { 1, 2, 3 });
        using var guard = StartGuard();
        WaitFor(() => _store.Versions("savegame0.baronysave").Count == 1);
    }

    [Fact]
    public void RestoresSaveDeletedByTheGame()
    {
        var path = SavePath("savegame0.baronysave");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        using var guard = StartGuard();
        WaitFor(() => _store.Versions("savegame0.baronysave").Count == 1);

        File.Delete(path);

        WaitFor(() => File.Exists(path));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        WaitFor(() => _events.Any(e => e.Kind == GuardEventKind.Restored));
    }

    [Fact]
    public void RestoresLatestVersionAfterSeveralChanges()
    {
        var path = SavePath("savegame1.baronysave");
        File.WriteAllBytes(path, new byte[] { 1 });
        using var guard = StartGuard();
        WaitFor(() => _store.Versions("savegame1.baronysave").Count == 1);

        File.WriteAllBytes(path, new byte[] { 2, 2 });
        WaitFor(() => _store.Versions("savegame1.baronysave").Count == 2);

        File.Delete(path);
        WaitFor(() => File.Exists(path));
        Assert.Equal(new byte[] { 2, 2 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void KeepsNewGameWrittenRightAfterDeletion()
    {
        var path = SavePath("savegame0.baronysave");
        File.WriteAllBytes(path, new byte[] { 1, 1, 1 });
        using var guard = StartGuard();
        WaitFor(() => _store.Versions("savegame0.baronysave").Count == 1);

        // The game deletes the old run and immediately starts a new one in the same slot.
        File.Delete(path);
        File.WriteAllBytes(path, new byte[] { 9, 9 });

        WaitFor(() => _store.Versions("savegame0.baronysave").Count == 2);
        Thread.Sleep(800);
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(path));
        // The old run is still in the history.
        Assert.Equal(new byte[] { 1, 1, 1 }, File.ReadAllBytes(_store.Versions("savegame0.baronysave")[1].Path));
    }

    [Fact]
    public void DoesNotRestoreWhenPaused()
    {
        var path = SavePath("savegame0.baronysave");
        File.WriteAllBytes(path, new byte[] { 1 });
        using var guard = StartGuard(autoRestore: false);
        WaitFor(() => _store.Versions("savegame0.baronysave").Count == 1);

        File.Delete(path);

        WaitFor(() => _events.Any(e => e.Kind == GuardEventKind.DeletedNotRestored));
        Thread.Sleep(800);
        Assert.False(File.Exists(path));
        Assert.Single(_store.Versions("savegame0.baronysave"));
    }

    [Fact]
    public void PicksUpSavesCreatedWhileRunning()
    {
        using var guard = StartGuard();
        var path = SavePath("savegame3.baronysave");
        File.WriteAllBytes(path, new byte[] { 4, 5 });
        WaitFor(() => _store.Versions("savegame3.baronysave").Count == 1);

        File.Delete(path);
        WaitFor(() => File.Exists(path));
    }

    [Fact]
    public void IgnoresOtherFiles()
    {
        using var guard = StartGuard();
        File.WriteAllText(SavePath("notes.txt"), "hi");
        Thread.Sleep(800);
        Assert.Empty(_store.SaveNames());
    }

    [Fact]
    public void ManualRestoreBacksUpCurrentFileFirst()
    {
        var path = SavePath("savegame0.baronysave");
        File.WriteAllBytes(path, new byte[] { 1 });
        var first = _store.Add("savegame0.baronysave", new byte[] { 1 })!;
        File.WriteAllBytes(path, new byte[] { 2 });

        var guard = new SaveGuard(_saves, _store);
        guard.RestoreVersion(first);

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
        var versions = _store.Versions("savegame0.baronysave");
        Assert.Equal(2, versions.Count);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(versions[0].Path));
    }

    [Fact]
    public void WaitsForSaveFolderToAppear()
    {
        Directory.Delete(_saves);
        using var guard = StartGuard();
        WaitFor(() => _events.Any(e => e.Kind == GuardEventKind.Error));

        Directory.CreateDirectory(_saves);
        File.WriteAllBytes(SavePath("savegame0.baronysave"), new byte[] { 7 });
        WaitFor(() => _store.Versions("savegame0.baronysave").Count == 1);
    }
}
