namespace BaronySaveGuard.Tests;

public sealed class BackupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bsg-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void SkipsUnchangedContent()
    {
        var store = new BackupStore(_root, 10);
        Assert.NotNull(store.Add("a.baronysave", new byte[] { 1 }));
        Assert.Null(store.Add("a.baronysave", new byte[] { 1 }));
        Assert.Single(store.Versions("a.baronysave"));
    }

    [Fact]
    public void KeepsOnlyNewestVersions()
    {
        var store = new BackupStore(_root, 3);
        for (byte i = 0; i < 6; i++) store.Add("a.baronysave", new[] { i });

        var versions = store.Versions("a.baronysave");
        Assert.Equal(3, versions.Count);
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(versions[0].Path));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(versions[2].Path));
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var path = Path.Combine(_root, "settings.json");
        new Settings { SaveFolder = "x", AutoRestore = false, MaxVersionsPerSave = 7 }.Save(path);
        var loaded = Settings.Load(path);
        Assert.Equal("x", loaded.SaveFolder);
        Assert.False(loaded.AutoRestore);
        Assert.Equal(7, loaded.MaxVersionsPerSave);
    }
}
