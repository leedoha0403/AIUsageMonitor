using AIUsage.Core.Handoff;
using AIUsage.Core.Storage;
using AIUsage.Presentation.Handoff;
using AIUsage.Presentation.ViewModels;
using AIUsage.Widget;
using Dora.Widget.Abstractions;

namespace AIUsage.Tests;

// The whole hand-over between the Host-side handler and the app-side service, on a real named pipe.
public class DetachHandlerTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(5);

    private static string NewName() => "AIUsageTest.Detach." + Guid.NewGuid().ToString("N");

    private static DetachRequest Request(Action<AIUsage.Core.AppState>? change = null)
    {
        var state = AIUsage.Core.Defaults.CreateState();
        change?.Invoke(state);
        return new DetachRequest("inst-1", FeatureStateSnapshot.CurrentVersion, FeatureStateSnapshot.Capture(state).Serialize(),
            new WidgetRect(1500, 300, 330, 250), 144);
    }

    private sealed class App : IAsyncDisposable
    {
        public required UsageFeatureViewModel ViewModel;
        public required FakeSurface Surface;
        public required AppHandoffService Service;

        public static async Task<App> StartAsync(string pipe, bool adoptState)
        {
            UsageFeatureViewModel? vm = null;
            await WpfHost.Run(() => vm = new UsageFeatureViewModel(
                new StateStore(Path.Combine(Path.GetTempPath(), "aiusage-detach-" + Guid.NewGuid().ToString("N"))), new FakeUi()));
            var surface = new FakeSurface();
            var service = new AppHandoffService(pipe, vm!, surface, adoptState, "1.0", TimeSpan.FromSeconds(2));
            service.Start();
            return new App { ViewModel = vm!, Surface = surface, Service = service };
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            await WpfHost.Run(ViewModel.Dispose);
        }
    }

    private static AIUsageDetachHandler Handler(string pipe, Func<bool>? launch = null, TimeSpan? launchTimeout = null, TimeSpan? ack = null) =>
        new(pipe, launch ?? (() => false), launchTimeout ?? TimeSpan.FromMilliseconds(600), ack ?? TimeSpan.FromSeconds(2));

    [Fact]
    public async Task Detach_hands_state_and_position_to_a_running_app()
    {
        var pipe = NewName();
        await using var app = await App.StartAsync(pipe, adoptState: true);
        var handler = Handler(pipe);

        var ok = await handler.DetachAsync(Request(s => s.Settings.DisplayUsageAs = "Used"), CancellationToken.None);

        Assert.True(ok);
        Assert.True(handler.IsDetached);
        Assert.Equal((1500d, 300d, 330d, 250d, 144d), app.Surface.Shown);
        await WpfHost.Run(() => Assert.Equal("Used", app.ViewModel.DisplayUsageAs));
        await handler.ShutdownAsync();
    }

    [Fact]
    public async Task Detach_starts_the_app_when_nobody_is_listening()
    {
        var pipe = NewName();
        App? started = null;
        var handler = Handler(pipe, launch: () =>
        {
            // The handler's launcher is synchronous by design (it starts a process); block on the fake one here.
#pragma warning disable xUnit1031
            started = App.StartAsync(pipe, adoptState: true).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            return true;
        }, launchTimeout: Short);

        var ok = await handler.DetachAsync(Request(), CancellationToken.None);

        Assert.True(ok);
        Assert.NotNull(started!.Surface.Shown);
        await handler.ShutdownAsync();
        await started.DisposeAsync();
    }

    [Fact]
    public async Task Detach_fails_and_the_host_keeps_ownership_when_the_app_cannot_be_started()
    {
        var handler = Handler(NewName(), launch: () => false);

        Assert.False(await handler.DetachAsync(Request(), CancellationToken.None));
        Assert.False(handler.IsDetached);
    }

    [Fact]
    public async Task Detach_fails_when_the_app_was_started_but_never_listens()
    {
        var handler = Handler(NewName(), launch: () => true, launchTimeout: TimeSpan.FromMilliseconds(500));

        Assert.False(await handler.DetachAsync(Request(), CancellationToken.None));
        Assert.False(handler.IsDetached);
    }

    [Fact]
    public async Task Detach_fails_when_the_app_refuses_the_state()
    {
        var pipe = NewName();
        await using var app = await App.StartAsync(pipe, adoptState: true);
        var handler = Handler(pipe);
        var bad = Request() with { StateVersion = 999 };   // "written by a newer version"

        Assert.False(await handler.DetachAsync(bad, CancellationToken.None));
        Assert.False(handler.IsDetached);
        Assert.Null(app.Surface.Shown);
        await handler.ShutdownAsync();
    }

    [Fact]
    public async Task Detach_gives_up_and_revokes_when_the_app_never_acknowledges()
    {
        var pipe = NewName();
        var seen = new List<string>();
        var revoked = new TaskCompletionSource<bool>();
        await using var fake = new HandoffPipe.Listener(pipe, m =>
        {
            lock (seen) seen.Add(m.Type);
            if (m.Type == HandoffTypes.Hello) return Task.FromResult<HandoffMessage?>(new HandoffMessage { Type = HandoffTypes.Welcome, Accepted = true, Protocol = 1 });
            if (m.Type == HandoffTypes.Revoke) revoked.TrySetResult(true);
            return Task.FromResult<HandoffMessage?>(null);   // adopt: silence
        });
        fake.Start();
        var handler = Handler(pipe, ack: TimeSpan.FromMilliseconds(400));

        Assert.False(await handler.DetachAsync(Request(), CancellationToken.None));
        Assert.False(handler.IsDetached);
        await revoked.Task.WaitAsync(Short);
        await handler.ShutdownAsync();
    }

    [Fact]
    public async Task An_app_on_another_protocol_version_is_not_used()
    {
        var pipe = NewName();
        await using var fake = new HandoffPipe.Listener(pipe, m => Task.FromResult<HandoffMessage?>(
            m.Type == HandoffTypes.Hello ? new HandoffMessage { Type = HandoffTypes.Welcome, Accepted = false, Reason = "protocol" } : null));
        fake.Start();
        var handler = Handler(pipe);

        Assert.False(await handler.DetachAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Dropping_the_app_widget_on_the_host_returns_ownership_with_the_latest_state()
    {
        var pipe = NewName();
        await using var app = await App.StartAsync(pipe, adoptState: true);
        var handler = Handler(pipe);
        DockRequest? got = null;
        handler.DockRequested += r =>
        {
            got = r;
            return Task.FromResult(true);
        };
        Assert.True(await handler.DetachAsync(Request(), CancellationToken.None));
        await WpfHost.Run(() => app.ViewModel.HistoryRange = "30D");   // the user changed something while it was out

        var docked = await WpfHost.Call(() => app.Service.RequestDockAsync(900, 700));

        Assert.True(docked);
        Assert.False(handler.IsDetached);
        Assert.Equal(new WidgetPoint(900, 700), got!.ScreenCursor);
        Assert.Equal("30D", FeatureStateSnapshot.Deserialize(got.StateVersion, got.StateJson)!.Settings.HistoryRange);
        await handler.ShutdownAsync();
    }

    [Fact]
    public async Task A_standalone_app_can_be_docked_without_any_prior_detach()
    {
        var pipe = NewName();
        await using var app = await App.StartAsync(pipe, adoptState: false);   // started on its own
        var launched = false;
        var handler = Handler(pipe, launch: () => { launched = true; return false; });
        DockRequest? got = null;
        handler.DockRequested += r => { got = r; return Task.FromResult(true); };

        await handler.StartAsync(CancellationToken.None);   // Host start-up / app announcement
        var docked = await WpfHost.Call(() => app.Service.RequestDockAsync(900, 700));

        Assert.True(docked);
        Assert.False(launched);   // probing never starts the app
        Assert.Equal(new WidgetPoint(900, 700), got!.ScreenCursor);
        Assert.False(handler.IsDetached);
        await handler.ShutdownAsync();
    }

    [Fact]
    public async Task Probing_with_no_app_running_does_nothing_and_can_be_repeated()
    {
        var handler = Handler(NewName());
        await handler.StartAsync(CancellationToken.None);
        await handler.StartAsync(CancellationToken.None);
        Assert.False(handler.IsDetached);
        await handler.ShutdownAsync();
    }

    [Fact]
    public async Task A_declined_dock_leaves_the_app_as_the_owner()
    {
        var pipe = NewName();
        await using var app = await App.StartAsync(pipe, adoptState: true);
        var handler = Handler(pipe);
        handler.DockRequested += _ => Task.FromResult(false);
        Assert.True(await handler.DetachAsync(Request(), CancellationToken.None));

        Assert.False(await WpfHost.Call(() => app.Service.RequestDockAsync(1, 1)));
        Assert.True(handler.IsDetached);
        await handler.ShutdownAsync();
    }

    [Fact]
    public async Task Hover_over_the_host_is_forwarded_and_ends()
    {
        var pipe = NewName();
        await using var app = await App.StartAsync(pipe, adoptState: true);
        var handler = Handler(pipe);
        var events = new List<WidgetPoint?>();
        handler.DockHover += p => { lock (events) events.Add(p); };
        Assert.True(await handler.DetachAsync(Request(), CancellationToken.None));

        await app.Service.SendDockHoverAsync(40, 50, over: true);
        await app.Service.SendDockHoverAsync(0, 0, over: false);

        var deadline = DateTime.UtcNow + Short;
        while (DateTime.UtcNow < deadline) { lock (events) { if (events.Count >= 2) break; } await Task.Delay(20); }
        lock (events)
        {
            Assert.Equal(new WidgetPoint?[] { new WidgetPoint(40, 50), null }, events);
        }
        await handler.ShutdownAsync();
    }

    [Fact]
    public async Task The_app_quitting_while_detached_ends_the_detached_state()
    {
        var pipe = NewName();
        var app = await App.StartAsync(pipe, adoptState: true);
        var handler = Handler(pipe);
        var ended = new TaskCompletionSource<bool>();
        handler.DetachEnded += () => ended.TrySetResult(true);
        Assert.True(await handler.DetachAsync(Request(), CancellationToken.None));

        await app.DisposeAsync();   // the process goes away

        await ended.Task.WaitAsync(Short);
        Assert.False(handler.IsDetached);
    }

    [Fact]
    public async Task A_second_detach_reuses_the_connection_after_docking()
    {
        var pipe = NewName();
        await using var app = await App.StartAsync(pipe, adoptState: true);
        var handler = Handler(pipe);
        handler.DockRequested += _ => Task.FromResult(true);

        Assert.True(await handler.DetachAsync(Request(), CancellationToken.None));
        Assert.True(await WpfHost.Call(() => app.Service.RequestDockAsync(1, 1)));
        Assert.True(await handler.DetachAsync(Request(s => s.Settings.HistoryRange = "6H"), CancellationToken.None));
        await WpfHost.Run(() => Assert.Equal("6H", app.ViewModel.HistoryRange));
        await handler.ShutdownAsync();
    }

    [Fact]
    public void Handler_has_a_parameterless_constructor_for_the_plugin_loader()
    {
        Assert.NotNull(typeof(AIUsageDetachHandler).GetConstructor(Type.EmptyTypes));
        Assert.Equal(AIUsageWidgetManifest.WidgetId, new AIUsageDetachHandler().WidgetId);
    }
}
