namespace BaronySaveGuard.App;

/// <summary>Pick a save and one of its backed up versions, and put it back.</summary>
internal sealed class RestoreForm : Form
{
    private readonly BackupStore _store;
    private readonly Func<SaveGuard?> _guard;
    private readonly ListBox _saves;
    private readonly ListView _versions;
    private readonly Button _restore;

    public RestoreForm(BackupStore store, Func<SaveGuard?> guard)
    {
        _store = store;
        _guard = guard;

        Text = "Barony Save Guard - Restore a save";
        Size = new Size(680, 440);
        MinimumSize = new Size(520, 320);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Shield;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8, 8, 8, 0),
            Text = "Pick a save on the left and a backup on the right (newest first). " +
                   "Go back to Barony's main menu before restoring, then reopen the load screen.",
        };

        _saves = new ListBox { Dock = DockStyle.Left, Width = 200, IntegralHeight = false };
        _saves.SelectedIndexChanged += (_, _) => LoadVersions();

        _versions = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
        };
        _versions.Columns.Add("Backed up", 220);
        _versions.Columns.Add("Size", 100, HorizontalAlignment.Right);
        _versions.SelectedIndexChanged += (_, _) => _restore!.Enabled = _versions.SelectedItems.Count == 1;
        _versions.DoubleClick += (_, _) => RestoreSelected();

        _restore = new Button { Text = "Restore this version", AutoSize = true, Enabled = false };
        _restore.Click += (_, _) => RestoreSelected();
        var openFolder = new Button { Text = "Open backups folder", AutoSize = true };
        openFolder.Click += (_, _) => TrayContext.OpenFolder(_store.Root);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(_restore);
        buttons.Controls.Add(openFolder);

        Controls.Add(_versions);
        Controls.Add(new Splitter { Dock = DockStyle.Left });
        Controls.Add(_saves);
        Controls.Add(buttons);
        Controls.Add(hint);

        RefreshLists();
    }

    public void RefreshLists()
    {
        var selected = _saves.SelectedItem as string;
        _saves.BeginUpdate();
        _saves.Items.Clear();
        foreach (var name in _store.SaveNames()) _saves.Items.Add(name);
        _saves.EndUpdate();
        if (selected != null && _saves.Items.Contains(selected)) _saves.SelectedItem = selected;
        else if (_saves.Items.Count > 0) _saves.SelectedIndex = 0;
        else LoadVersions();
    }

    private void LoadVersions()
    {
        _versions.BeginUpdate();
        _versions.Items.Clear();
        if (_saves.SelectedItem is string name)
        {
            foreach (var v in _store.Versions(name))
            {
                var item = new ListViewItem(v.CreatedLocal.ToString("yyyy-MM-dd  HH:mm:ss")) { Tag = v };
                item.SubItems.Add($"{v.Size / 1024.0:0.0} KB");
                _versions.Items.Add(item);
            }
        }
        _versions.EndUpdate();
        _restore.Enabled = false;
    }

    private void RestoreSelected()
    {
        if (_versions.SelectedItems.Count != 1 || _versions.SelectedItems[0].Tag is not BackupVersion version) return;
        var guard = _guard();
        if (guard == null)
        {
            MessageBox.Show(this, "Barony's save folder hasn't been found yet. Start the game or choose the folder from the tray menu.",
                "Barony Save Guard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var answer = MessageBox.Show(this,
            $"Put back {version.SaveName} as it was at {version.CreatedLocal:g}?\n\nThe current file is backed up first, so nothing is lost.",
            "Restore save", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        try
        {
            guard.RestoreVersion(version);
            RefreshLists();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't restore: {ex.Message}", "Barony Save Guard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
