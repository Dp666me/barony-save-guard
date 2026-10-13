namespace BaronySaveGuard.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Steam launch option: "...\BaronySaveGuard.exe" %command%
        var launchGame = GameLauncher.IsLaunchCommand(args);

        using var mutex = new Mutex(true, @"Local\BaronySaveGuard", out var firstInstance);
        if (!firstInstance && launchGame)
        {
            // Already running in the tray (e.g. "Start with Windows"), which protects the saves; just start the game.
            GameLauncher.Start(args);
            return;
        }
        if (!firstInstance)
        {
            MessageBox.Show("Barony Save Guard is already running. Look for its icon in the system tray.",
                "Barony Save Guard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        GameLauncher? game = null;
        if (launchGame)
        {
            try
            {
                game = GameLauncher.Start(args);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't start Barony: {ex.Message}", "Barony Save Guard",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext(game));
    }
}
