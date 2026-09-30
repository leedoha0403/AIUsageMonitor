using AIUsage.Core.Handoff;
using AIUsage.Core.Storage;
using AIUsage.Presentation.Handoff;
using AIUsage.Presentation.ViewModels;

namespace AIUsage.Tests;

internal sealed class FakeUi : IUiServices
{
    public bool Confirm(string title, string message) => false;
    public string? PickFolder(string description, string initialDirectory) => null;
    public bool EditSchedule(ScheduleEditorViewModel editor) => false;
    public void Post(Action action) => action();
    public bool IsGranted(UsageCapability capability) => true;
    public Task<bool> RequestAsync(UsageCapability capability) => Task.FromResult(true);
}

internal sealed class FakeSurface : IHandoffSurface
{
    public (double X, double Y, double W, double H, double Dpi)? Shown;
    public bool Hidden;
    public Task RunOnUiAsync(Action action) => WpfHost.Run(action);
    public void ShowAt(double x, double y, double width, double height, double dpi) => Shown = (x, y, width, height, dpi);
    public void Hide() => Hidden = true;
}

// The standalone app's end of the hand-over, against a Host stand-in on the real pipe.
public class AppHandoffServiceTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(5);

    private static string NewName() => "AIUsageTest.Handoff." + Guid.NewGuid().ToString("N");

    private static UsageFeatureViewModel NewViewModel() =>
        new(new StateStore(Path.Combine(Path.GetTempPath(), "aiusage-handoff-" + Guid.NewGuid().ToString("N"))), new FakeUi());

    private static string StateWith(Action<AIUsage.Core.AppState> change)
    {
        var state = AIUsage.Core.Defaults.CreateState();
        change(state);
        return FeatureStateSnapshot.Capture(state).Serialize();
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required string Name;
        public required UsageFeatureViewModel ViewModel;
        public required FakeSurface Surface;
        public required AppHandoffService Service;
        public HandoffChannel? Host;

        public static async Task<Rig> StartAsync(bool adoptState, TimeSpan? ackTimeout = null, Func<HandoffMessage, Task<HandoffMessage?>>? hostHandler = null)
        {
            var name = NewName();
            UsageFeatureViewModel? vm = null;
            await WpfHost.Run(() => vm = NewViewModel());
            var surface = new FakeSurface();
            var service = new AppHandoffService(name, vm!, surface, adoptState, "1.2.3", ackTimeout);
            service.Start();
            var rig = new Rig { Name = name, ViewModel = vm!, Surface = surface, Service = service };
            rig.Host = await HandoffPipe.ConnectAsync(name, Short, hostHandler);
            return rig;
        }

        public async ValueTask DisposeAsync()
        {
            if (Host != null) await Host.DisposeAsync();
            await Service.DisposeAsync();
            await WpfHost.Run(ViewModel.Dispose);
        }
    }

    [Fact]
    public async Task Hello_is_answered_with_the_app_version_and_protocol()
    {
        await using var rig = await Rig.StartAsync(adoptState: true);
        var reply = await rig.Host!.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello, Protocol = HandoffTypes.ProtocolVersion, Pid = 77 }, Short);

        Assert.Equal(HandoffTypes.Welcome, reply.Type);
        Assert.True(reply.Accepted);
        Assert.Equal("1.2.3", reply.AppVersion);
        Assert.Equal(77, rig.Service.HostPid);
    }

    [Fact]
    public async Task A_different_protocol_version_is_declined_at_hello()
    {
        await using var rig = await Rig.StartAsync(adoptState: true);
        var reply = await rig.Host!.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello, Protocol = 99 }, Short);

        Assert.False(reply.Accepted);
        Assert.Equal("protocol", reply.Reason);
    }

    [Fact]
    public async Task Adopt_applies_the_hosts_state_and_shows_the_widget_at_the_drop_position()
    {
        await using var rig = await Rig.StartAsync(adoptState: true);
        var json = StateWith(s =>
        {
            s.Settings.DisplayUsageAs = "Used";
            s.Settings.HistoryRange = "7D";
            s.Settings.FavoriteProvider = "codex";
            s.Providers["claude"].AccountName = "Work";
        });

        var reply = await rig.Host!.RequestAsync(new HandoffMessage
        {
            Type = HandoffTypes.Adopt, StateVersion = FeatureStateSnapshot.CurrentVersion, StateJson = json,
            X = 1800, Y = 240, Width = 330, Height = 250, Dpi = 144
        }, Short);

        Assert.True(reply.Accepted);
        Assert.Equal((1800d, 240d, 330d, 250d, 144d), rig.Surface.Shown);
        await WpfHost.Run(() =>
        {
            Assert.Equal("Used", rig.ViewModel.DisplayUsageAs);
            Assert.Equal("7D", rig.ViewModel.HistoryRange);
            Assert.Equal("codex", rig.ViewModel.FavoriteProvider);
            Assert.Equal("Work", rig.ViewModel.Providers.First(p => p.AccountKey == "claude").AccountName);
        });
    }

    [Fact]
    public async Task An_app_that_was_already_running_keeps_its_own_state_but_still_shows_the_widget()
    {
        await using var rig = await Rig.StartAsync(adoptState: false);
        var json = StateWith(s => s.Settings.DisplayUsageAs = "Used");

        var reply = await rig.Host!.RequestAsync(new HandoffMessage
        {
            Type = HandoffTypes.Adopt, StateVersion = FeatureStateSnapshot.CurrentVersion, StateJson = json, X = 10, Y = 20, Width = 300, Height = 200
        }, Short);

        Assert.True(reply.Accepted);
        Assert.Equal("state-kept", reply.Reason);
        Assert.NotNull(rig.Surface.Shown);
        await WpfHost.Run(() => Assert.Equal("Remaining", rig.ViewModel.DisplayUsageAs));
    }

    [Theory]
    [InlineData("{ not json", 1)]
    [InlineData("{}", 999)]   // written by a newer version
    public async Task Unusable_state_is_refused_and_nothing_changes(string json, int version)
    {
        await using var rig = await Rig.StartAsync(adoptState: true);
        var reply = await rig.Host!.RequestAsync(new HandoffMessage { Type = HandoffTypes.Adopt, StateVersion = version, StateJson = json }, Short);

        Assert.False(reply.Accepted);
        Assert.Equal("state", reply.Reason);
        Assert.Null(rig.Surface.Shown);
    }

    [Fact]
    public async Task Revoke_hides_the_widget_again()
    {
        await using var rig = await Rig.StartAsync(adoptState: true);
        var reply = await rig.Host!.RequestAsync(new HandoffMessage { Type = HandoffTypes.Revoke }, Short);

        Assert.True(reply.Accepted);
        Assert.True(rig.Surface.Hidden);
    }

    [Fact]
    public async Task Dock_request_carries_the_current_state_and_succeeds_when_the_host_accepts()
    {
        HandoffMessage? seen = null;
        await using var rig = await Rig.StartAsync(adoptState: true, hostHandler: m =>
        {
            seen = m;
            return Task.FromResult<HandoffMessage?>(new HandoffMessage { Type = HandoffTypes.Docked, Accepted = true });
        });
        await rig.Host!.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello, Protocol = 1 }, Short);
        await WpfHost.Run(() => rig.ViewModel.DisplayUsageAs = "Used");

        var docked = await WpfHost.Call(() => rig.Service.RequestDockAsync(500, 600));

        Assert.True(docked);
        Assert.Equal(HandoffTypes.DockRequest, seen!.Type);
        Assert.Equal(500, seen.CursorX);
        var carried = FeatureStateSnapshot.Deserialize(seen.StateVersion, seen.StateJson!);
        Assert.Equal("Used", carried!.Settings.DisplayUsageAs);
    }

    [Fact]
    public async Task Dock_request_fails_when_the_host_refuses_and_the_app_stays_the_owner()
    {
        await using var rig = await Rig.StartAsync(adoptState: true, hostHandler: m =>
            Task.FromResult<HandoffMessage?>(new HandoffMessage { Type = HandoffTypes.Docked, Accepted = false, Reason = "full" }));
        await rig.Host!.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello, Protocol = 1 }, Short);

        Assert.False(await WpfHost.Call(() => rig.Service.RequestDockAsync(1, 1)));
    }

    [Fact]
    public async Task Dock_request_times_out_when_the_host_never_answers()
    {
        await using var rig = await Rig.StartAsync(adoptState: true, ackTimeout: TimeSpan.FromMilliseconds(300),
            hostHandler: m => Task.FromResult<HandoffMessage?>(null));
        await rig.Host!.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello, Protocol = 1 }, Short);

        Assert.False(await WpfHost.Call(() => rig.Service.RequestDockAsync(1, 1)));
    }

    [Fact]
    public async Task Dock_request_without_a_connected_host_is_a_no_op()
    {
        var name = NewName();
        UsageFeatureViewModel? vm = null;
        await WpfHost.Run(() => vm = NewViewModel());
        await using var service = new AppHandoffService(name, vm!, new FakeSurface(), true, "1");
        service.Start();

        Assert.False(service.HostConnected);
        Assert.False(await WpfHost.Call(() => service.RequestDockAsync(1, 1)));
        await WpfHost.Run(vm!.Dispose);
    }

    [Fact]
    public async Task Hover_messages_reach_the_host()
    {
        var got = new List<string>();
        await using var rig = await Rig.StartAsync(adoptState: true, hostHandler: m =>
        {
            lock (got) got.Add(m.Type);
            return Task.FromResult<HandoffMessage?>(null);
        });
        await rig.Host!.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello, Protocol = 1 }, Short);

        await rig.Service.SendDockHoverAsync(10, 20, over: true);
        await rig.Service.SendDockHoverAsync(0, 0, over: false);

        var deadline = DateTime.UtcNow + Short;
        while (DateTime.UtcNow < deadline) { lock (got) { if (got.Count >= 2) break; } await Task.Delay(20); }
        lock (got) Assert.Equal(new[] { HandoffTypes.DockHover, HandoffTypes.DockHoverEnd }, got);
    }
}
