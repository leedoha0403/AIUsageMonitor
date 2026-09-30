namespace UsageMonitorWpf;

public partial class App : System.Windows.Application
{
    // Launched by the Windows sign-in entry: start quietly (tray/chips) instead of opening the dashboard.
    public static bool StartedAtSignIn { get; private set; }

    // Started by a Host that is handing a widget over: stay quiet until the hand-over arrives, and take the
    // Host's state (this process has no data of its own yet worth keeping).
    public const string AdoptArgument = "--adopt";
    public static bool LaunchedForAdopt { get; private set; }

    // Fixed GUID-based names so a second launch reliably finds the first instance's mutex/event.
    private const string MutexName = AIUsage.Core.AppIdentity.StandaloneMutexName;
    private const string ShowEventName = "Local\\UsageMonitorWpf-ShowDashboard-9F1E7B2D-6C3A-4E4A-9E1D-2E9B6D6C1A11";

    private System.Threading.Mutex? _singleInstanceMutex;
    private System.Threading.EventWaitHandle? _showEvent;
    private bool _ownsMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Without these, any exception outside startup (timer tick, save, refresh) ends the process
        // silently with nothing in the log.
        DispatcherUnhandledException += (_, args) =>
        {
            Log("unhandled UI exception (kept running): " + args.Exception);
            args.Handled = true;
        };
        System.AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("fatal unhandled exception: " + args.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("unobserved task exception: " + args.Exception);
            args.SetObserved();
        };

        // Temp copy launched by SelfUpdater: swap the exe and restart it, without touching the single-instance mutex.
        if (e.Args.Length == 4 && e.Args[0] == Shell.SelfUpdater.ApplyArgument)
        {
            Shell.SelfUpdater.RunHelper(e.Args);
            Shutdown();
            return;
        }
        Shell.SelfUpdater.CleanupBackup();

        try
        {
            base.OnStartup(e);

            LaunchedForAdopt = e.Args.Contains(AdoptArgument, System.StringComparer.OrdinalIgnoreCase);
            _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out var createdNew);
            _ownsMutex = createdNew;
            if (!createdNew)
            {
                Log("duplicate instance detected; asking the running instance to show itself");
                try
                {
                    // A Host start-up race must not pop the running app's dashboard open.
                    if (!LaunchedForAdopt)
                    {
                        using var existingShowEvent = System.Threading.EventWaitHandle.OpenExisting(ShowEventName);
                        existingShowEvent.Set();
                    }
                }
                catch (System.Exception ex)
                {
                    Log("could not signal running instance: " + ex);
                }
                Shutdown();
                return;
            }

            _showEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ShowEventName);
            StartShowRequestListener();

            StartedAtSignIn = e.Args.Contains(Shell.StartupService.StartupArgument, System.StringComparer.OrdinalIgnoreCase) || LaunchedForAdopt;
            Shell.InstallLocation.Register();
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

    // A later launch that lost the single-instance race sets this event instead of starting its own process.
    private void StartShowRequestListener()
    {
        var showEvent = _showEvent;
        if (showEvent == null) return;
        var thread = new System.Threading.Thread(() =>
        {
            while (true)
            {
                try
                {
                    showEvent.WaitOne();
                }
                catch (System.ObjectDisposedException)
                {
                    return;
                }
                Dispatcher.Invoke(() => (MainWindow as MainWindow)?.ShowDashboard());
            }
        })
        {
            IsBackground = true,
            Name = "ShowRequestListener"
        };
        thread.Start();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Log("exit (code " + e.ApplicationExitCode + ")");
        _showEvent?.Dispose();
        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void Log(string message) => AIUsage.Core.AppLog.Write(message);
}
