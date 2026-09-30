using System.Windows;
using System.Windows.Threading;

namespace AIUsage.Tests;

// One WPF Application per process, on its own STA thread; tests run their UI work on its dispatcher.
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> Instance = new(Start);

    private static Dispatcher Start()
    {
        var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // Deliberately no AIUsage resources: the views must work inside a Host that knows nothing about them.
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "WpfHost" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    public static Task Run(Func<Task> work) => Instance.Value.InvokeAsync(work).Task.Unwrap();

    public static Task Run(Action work) => Instance.Value.InvokeAsync(work).Task;

    public static Task<T> Call<T>(Func<Task<T>> work) => Instance.Value.InvokeAsync(work).Task.Unwrap();
}
