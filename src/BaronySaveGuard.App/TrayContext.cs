namespace BaronySaveGuard.App;

/// <summary>The app lives in the system tray; this owns the icon, its menu, and the guard.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private const int MaxLogLines = 500;

    private readonly Settings _settings = Settings.Load();
    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly System.Windows.Forms.Timer _findFolderTimer;
    private readonly List<GuardEvent> _log = new();
    private readonly string _logPath = Path.Combine(Settings.AppDataFolder, "log.txt");
    private BackupStore _store;
    private SaveGuard? _guard;
    private RestoreForm? _restoreForm;
    private LogForm? _logForm;
    private readonly GameLauncher? _game;
    private readonly System.Windows.Forms.Timer? _gameExitTimer;
    private DateTime? _gameExitedAt;

    /// <summary>How long to keep guarding after the game closes, so a last-second deletion is still handled.</summary>
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(5);

    /// <param name="game">Set when started from Steam launch options; the app then closes with the game.</param>
    public TrayContext(GameLauncher? game = null)
    {
        _game = game;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _store = new BackupStore(_settings.EffectiveBackupFolder, _settings.MaxVersionsPerSave);

        _statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("Pause auto-restore", null, (_, _) => TogglePause());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Restore a save...", null, (_, _) => ShowRestore()) { Font = new Font(menu.Font, FontStyle.Bold) },
            new ToolStripMenuItem("Open backups folder", null, (_, _) => OpenFolder(_store.Root)),
            new ToolStripMenuItem("Show activity", null, (_, _) => ShowLog()),
            new ToolStripSeparator(),
            _pauseItem,
            _startupItem,
            new ToolStripMenuItem("Change save folder...", null, (_, _) => ChooseFolder()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        });
        menu.Opening += (_, _) => RefreshMenu();

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Barony Save Guard",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowRestore();

        _findFolderTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _findFolderTimer.Tick += (_, _) => TryStartGuard(announce: true);

        if (_game?.SaveFolder != null && _settings.SaveFolder != _game.SaveFolder)
        {
            // The folder next to the game Steam just launched is the most reliable guess there is.
            _settings.SaveFolder = _game.SaveFolder;
            _settings.Save();
        }
        if (_game != null)
        {
            _gameExitTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _gameExitTimer.Tick += (_, _) => CheckGameExited();
            _gameExitTimer.Start();
        }

        if (!TryStartGuard(announce: false))
        {
            AddLog(new GuardEvent(GuardEventKind.Info, "Barony's save folder wasn't found yet. Start the game and it will be picked up."));
            _icon.ShowBalloonTip(5000, "Barony Save Guard",
                "Start Barony and your saves will be protected automatically.", ToolTipIcon.Info);
            _findFolderTimer.Start();
        }
        else
        {
            _icon.ShowBalloonTip(3000, "Barony Save Guard", "Your Barony saves are protected.", ToolTipIcon.Info);
        }
        RefreshMenu();
    }

    private void CheckGameExited()
    {
        if (_game!.GameStillRunning())
        {
            _gameExitedAt = null;
            return;
        }
        _gameExitedAt ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _gameExitedAt >= ExitGrace)
        {
            AddLog(new GuardEvent(GuardEventKind.Info, "Barony closed. Closing Barony Save Guard."));
            ExitThread();
        }
    }

    private bool TryStartGuard(bool announce)
    {
        var folder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? SaveFolderLocator.Find() : _settings.SaveFolder;
        if (folder == null) return false;

        _findFolderTimer.Stop();
        if (_settings.SaveFolder != folder)
        {
            // Remember it, so protection starts right away next time even before the game runs.
            _settings.SaveFolder = folder;
            _settings.Save();
        }
        StartGuard(folder);
        if (announce)
            _icon.ShowBalloonTip(3000, "Barony Save Guard", "Found your saves. They're protected now.", ToolTipIcon.Info);
        return true;
    }

    private void StartGuard(string folder)
    {
        _guard?.Dispose();
        _guard = new SaveGuard(folder, _store) { AutoRestore = _settings.AutoRestore };
        _guard.Event += e => _ui.Post(_ => OnGuardEvent(e), null);
        _guard.Start();
        RefreshMenu();
    }

    private void OnGuardEvent(GuardEvent e)
    {
        AddLog(e);
        switch (e.Kind)
        {
            case GuardEventKind.Restored:
                _icon.ShowBalloonTip(5000, "Save restored", e.Message, ToolTipIcon.Info);
                break;
            case GuardEventKind.DeletedNotRestored:
                _icon.ShowBalloonTip(5000, "Save deleted", e.Message, ToolTipIcon.Warning);
                break;
            case GuardEventKind.Error:
                _icon.ShowBalloonTip(5000, "Barony Save Guard", e.Message, ToolTipIcon.Error);
                break;
        }
        _restoreForm?.RefreshLists();
    }

    private void AddLog(GuardEvent e)
    {
        _log.Add(e);
        if (_log.Count > MaxLogLines) _log.RemoveAt(0);
        _logForm?.Append(e);
        try
        {
            Directory.CreateDirectory(Settings.AppDataFolder);
            File.AppendAllText(_logPath, $"{e.Time:yyyy-MM-dd HH:mm:ss} {e.Message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    private void RefreshMenu()
    {
        _statusItem.Text = _guard == null
            ? "Waiting for Barony..."
            : _settings.AutoRestore ? "Protecting your saves" : "Backing up only (auto-restore paused)";
        _pauseItem.Checked = !_settings.AutoRestore;
        try { _startupItem.Checked = Startup.IsEnabled; } catch { _startupItem.Enabled = false; }
        _icon.Text = _statusItem.Text.Length > 63 ? _statusItem.Text[..63] : _statusItem.Text;
    }

    private void TogglePause()
    {
        _settings.AutoRestore = !_settings.AutoRestore;
        _settings.Save();
        if (_guard != null) _guard.AutoRestore = _settings.AutoRestore;
        AddLog(new GuardEvent(GuardEventKind.Info, _settings.AutoRestore ? "Auto-restore resumed." : "Auto-restore paused. Saves are still backed up."));
        RefreshMenu();
    }

    private void ToggleStartup()
    {
        try
        {
            Startup.Set(!Startup.IsEnabled);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't change the startup setting: {ex.Message}", "Barony Save Guard");
        }
        RefreshMenu();
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose Barony's \"savegames\" folder",
            UseDescriptionForTitle = true,
            InitialDirectory = _guard?.SaveFolder ?? "",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _settings.SaveFolder = dialog.SelectedPath;
        _settings.Save();
        _findFolderTimer.Stop();
        StartGuard(dialog.SelectedPath);
    }

    private void ShowRestore()
    {
        if (_restoreForm == null || _restoreForm.IsDisposed)
        {
            _restoreForm = new RestoreForm(_store, () => _guard);
            _restoreForm.FormClosed += (_, _) => _restoreForm = null;
        }
        _restoreForm.Show();
        _restoreForm.Activate();
    }

    private void ShowLog()
    {
        if (_logForm == null || _logForm.IsDisposed)
        {
            _logForm = new LogForm(_log);
            _logForm.FormClosed += (_, _) => _logForm = null;
        }
        _logForm.Show();
        _logForm.Activate();
    }

    internal static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    protected override void ExitThreadCore()
    {
        _guard?.Dispose();
        _findFolderTimer.Dispose();
        _gameExitTimer?.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
