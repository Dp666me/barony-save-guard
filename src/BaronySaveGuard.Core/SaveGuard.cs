namespace BaronySaveGuard;

public enum GuardEventKind { Info, BackedUp, Restored, DeletedNotRestored, Error }

public sealed record GuardEvent(GuardEventKind Kind, string Message, string? SaveName = null)
{
    public DateTime Time { get; } = DateTime.Now;
    public override string ToString() => $"{Time:HH:mm:ss} {Message}";
}

/// <summary>
/// Watches the save folder, keeps a versioned backup of every save, and puts a save back when
/// it is deleted. All file work happens on one background loop, so events never race each other.
/// </summary>
public sealed class SaveGuard : IDisposable
{
    private readonly BackupStore _store;
    private readonly object _pendingLock = new();
    private readonly Dictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private FileSystemWatcher? _watcher;
    private DateTime _nextSweep = DateTime.MinValue;
    private bool _folderMissingReported;

    public string SaveFolder { get; }
    public BackupStore Store => _store;

    /// <summary>Put deleted saves back automatically.</summary>
    public bool AutoRestore { get; set; } = true;

    /// <summary>Wait after a change before backing up, so the game can finish writing.</summary>
    public TimeSpan ChangeSettleDelay { get; set; } = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Wait after a deletion before restoring. If the game writes a new save in the same slot
    /// within this time (e.g. "start new game"), that new save is kept and not overwritten.
    /// </summary>
    public TimeSpan RestoreDelay { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Full rescan of the folder as a safety net in case a watcher event is missed.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(3);

    public event Action<GuardEvent>? Event;

    public SaveGuard(string saveFolder, BackupStore store)
    {
        SaveFolder = saveFolder;
        _store = store;
    }

    public void Start()
    {
        if (_loop != null) return;
        _cts = new CancellationTokenSource();
        Raise(GuardEventKind.Info, $"Watching {SaveFolder}");
        Raise(GuardEventKind.Info, $"Backups go to {_store.Root}");
        _loop = Task.Run(() => Loop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        _watcher?.Dispose();
        _watcher = null;
        _loop = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Puts a chosen backup back into the save folder. The current file, if any, is backed up first
    /// so nothing is lost.
    /// </summary>
    public void RestoreVersion(BackupVersion version)
    {
        lock (_pendingLock)
        {
            var target = Path.Combine(SaveFolder, version.SaveName);
            var current = SaveFiles.TryRead(target);
            if (current != null) _store.Add(version.SaveName, current);
            SaveFiles.WriteAtomic(target, File.ReadAllBytes(version.Path), overwrite: true);
            _known.Add(version.SaveName);
            Raise(GuardEventKind.Restored, $"Restored {version.SaveName} from {version.CreatedLocal:g}", version.SaveName);
        }
    }

    private async Task Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Raise(GuardEventKind.Error, $"Unexpected error: {ex.Message}");
            }
            try { await Task.Delay(250, token); } catch (TaskCanceledException) { }
        }
    }

    private void Tick()
    {
        if (!Directory.Exists(SaveFolder))
        {
            if (!_folderMissingReported)
                Raise(GuardEventKind.Error, $"Save folder not found: {SaveFolder}. Waiting for it to appear.");
            _folderMissingReported = true;
            _watcher?.Dispose();
            _watcher = null;
            return;
        }
        if (_folderMissingReported)
        {
            _folderMissingReported = false;
            Raise(GuardEventKind.Info, "Save folder found.");
        }

        EnsureWatcher();

        var now = DateTime.UtcNow;
        if (now >= _nextSweep)
        {
            _nextSweep = now + SweepInterval;
            Sweep();
        }

        List<string> due;
        lock (_pendingLock)
        {
            due = _pending.Where(p => p.Value <= now).Select(p => p.Key).ToList();
            foreach (var name in due) _pending.Remove(name);
        }
        foreach (var name in due) Check(name);
    }

    private void EnsureWatcher()
    {
        if (_watcher != null) return;
        try
        {
            _watcher = new FileSystemWatcher(SaveFolder, "*" + SaveFiles.Extension)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            _watcher.Created += (_, e) => Schedule(e.Name, ChangeSettleDelay);
            _watcher.Changed += (_, e) => Schedule(e.Name, ChangeSettleDelay);
            _watcher.Deleted += (_, e) => Schedule(e.Name, RestoreDelay);
            _watcher.Renamed += (_, e) =>
            {
                Schedule(e.OldName, RestoreDelay);
                Schedule(e.Name, ChangeSettleDelay);
            };
            _watcher.Error += (_, _) =>
            {
                // Buffer overflow or folder gone: rebuild the watcher and rescan.
                _watcher?.Dispose();
                _watcher = null;
                _nextSweep = DateTime.MinValue;
            };
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _watcher = null;
            Raise(GuardEventKind.Error, $"Could not watch the save folder ({ex.Message}); relying on rescans.");
        }
    }

    private void Schedule(string? name, TimeSpan delay)
    {
        if (name == null || !SaveFiles.IsSaveFile(name)) return;
        name = Path.GetFileName(name);
        lock (_pendingLock)
        {
            var due = DateTime.UtcNow + delay;
            // A later event pushes the check back; this debounces bursts of writes.
            if (!_pending.TryGetValue(name, out var existing) || existing < due)
                _pending[name] = due;
        }
    }

    private void Sweep()
    {
        var present = Directory.EnumerateFiles(SaveFolder, "*" + SaveFiles.Extension)
            .Select(Path.GetFileName).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in present)
        {
            lock (_pendingLock) { if (_pending.ContainsKey(name)) continue; }
            Check(name);
        }

        foreach (var name in _known.Where(n => !present.Contains(n)).ToList())
        {
            // Missing on a sweep but no watcher event yet: give it the same grace period.
            lock (_pendingLock)
            {
                if (!_pending.ContainsKey(name)) _pending[name] = DateTime.UtcNow + RestoreDelay;
            }
        }
    }

    private void Check(string name)
    {
        lock (_pendingLock)
        {
            var path = Path.Combine(SaveFolder, name);
            if (File.Exists(path))
            {
                var content = SaveFiles.TryRead(path);
                if (content == null || content.Length == 0) return;
                _known.Add(name);
                var added = _store.Add(name, content);
                if (added != null)
                    Raise(GuardEventKind.BackedUp, $"Backed up {name}", name);
                return;
            }

            if (!_known.Remove(name)) return;

            var latest = _store.Latest(name);
            if (latest == null) return;

            if (!AutoRestore)
            {
                Raise(GuardEventKind.DeletedNotRestored,
                    $"{name} was deleted. Auto-restore is paused; the backup is still kept.", name);
                return;
            }

            if (SaveFiles.WriteAtomic(path, File.ReadAllBytes(latest.Path), overwrite: false))
            {
                _known.Add(name);
                Raise(GuardEventKind.Restored, $"{name} was deleted. Restored it from the backup made {latest.CreatedLocal:T}.", name);
            }
            else
            {
                // The game wrote a new save there in the meantime; keep it and back it up instead.
                _known.Add(name);
                Schedule(name, ChangeSettleDelay);
            }
        }
    }

    private void Raise(GuardEventKind kind, string message, string? saveName = null)
    {
        try { Event?.Invoke(new GuardEvent(kind, message, saveName)); } catch { }
    }
}
