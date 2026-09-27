namespace UsageMonitorWpf;

public partial class App : System.Windows.Application
{
    // Launched by the Windows sign-in entry: start quietly (tray/chips) instead of opening the dashboard.
    public static bool StartedAtSignIn { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);
            StartedAtSignIn = e.Args.Contains(Core.StartupService.StartupArgument, System.StringComparer.OrdinalIgnoreCase);
            Log(StartedAtSignIn ? "startup (sign-in)" : "startup");
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            var window = new MainWindow();
            MainWindow = window;
            // MainWindow applies the saved Mini/Expanded version itself (widget only in Mini).
            Log("main window created");
        }
        catch (System.Exception ex)
        {
            Log(ex.ToString());
            System.Windows.MessageBox.Show(ex.ToString(), "Usage Monitor startup error");
            Shutdown(-1);
        }
    }

    private static void Log(string message)
    {
        var dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "UsageMonitorWpf");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(dir, "startup.log"),
            $"[{System.DateTimeOffset.Now:O}] {message}{System.Environment.NewLine}");
    }
}
