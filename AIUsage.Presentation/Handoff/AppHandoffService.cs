using AIUsage.Core.Handoff;
using AIUsage.Core.Storage;
using AIUsage.Presentation.ViewModels;

namespace AIUsage.Presentation.Handoff;

// The window-side of a hand-over. Implemented by the standalone app's mini widget.
public interface IHandoffSurface
{
    // Runs on the UI thread; the service itself is driven from pipe threads.
    Task RunOnUiAsync(Action action);

    // Shows the mini widget at a virtual-screen position given in physical pixels for a monitor at this dpi.
    void ShowAt(double x, double y, double width, double height, double dpi);

    void Hide();
}

// Standalone-app end of the hand-over protocol. It listens on a named pipe; a Host connects and can hand a widget
// over (adopt) at any time. In the other direction, the widget window asks the connected Host to take ownership
// back (RequestDockAsync). Ownership is exclusive: whoever sent the state stops using it once the peer acks.
public sealed class AppHandoffService : IAsyncDisposable
{
    public static readonly TimeSpan DefaultAckTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _ackTimeout;

    private readonly UsageFeatureViewModel _viewModel;
    private readonly IHandoffSurface _surface;
    private readonly string _appVersion;
    private readonly HandoffPipe.Listener _listener;

    // adoptState: only a process that was started for the hand-over takes the Host's state. An app that was
    // already running has fresher data of its own (the Host's copy yielded collection to it), so it keeps it.
    private readonly bool _adoptState;

    public AppHandoffService(string pipeName, UsageFeatureViewModel viewModel, IHandoffSurface surface, bool adoptState, string appVersion, TimeSpan? ackTimeout = null)
    {
        _ackTimeout = ackTimeout ?? DefaultAckTimeout;
        _viewModel = viewModel;
        _surface = surface;
        _adoptState = adoptState;
        _appVersion = appVersion;
        _listener = new HandoffPipe.Listener(pipeName, OnRequestAsync);
    }

    public bool HostConnected => _listener.Current != null;

    // Raised when a Host says hello (it carries the Host's process id, for locating its window).
    public event Action<int>? HostGreeted;

    public int HostPid { get; private set; }

    public void Start() => _listener.Start();

    private async Task<HandoffMessage?> OnRequestAsync(HandoffMessage message)
    {
        switch (message.Type)
        {
            case HandoffTypes.Hello:
                HostPid = message.Pid;
                HostGreeted?.Invoke(message.Pid);
                return new HandoffMessage
                {
                    Type = HandoffTypes.Welcome,
                    Protocol = HandoffTypes.ProtocolVersion,
                    AppVersion = _appVersion,
                    Pid = Environment.ProcessId,
                    Accepted = message.Protocol == HandoffTypes.ProtocolVersion,
                    Reason = message.Protocol == HandoffTypes.ProtocolVersion ? null : "protocol"
                };

            case HandoffTypes.Adopt:
                return await AdoptAsync(message).ConfigureAwait(false);

            case HandoffTypes.Revoke:
                await _surface.RunOnUiAsync(_surface.Hide).ConfigureAwait(false);
                return new HandoffMessage { Type = HandoffTypes.Adopted, Accepted = true };

            default:
                return null;
        }
    }

    private async Task<HandoffMessage> AdoptAsync(HandoffMessage message)
    {
        FeatureStateSnapshot? snapshot = null;
        FeatureStateSnapshot? chipsOnly = null;
        if (!_adoptState && !string.IsNullOrEmpty(message.StateJson))
            chipsOnly = FeatureStateSnapshot.Deserialize(message.StateVersion, message.StateJson);
        if (_adoptState && !string.IsNullOrEmpty(message.StateJson))
        {
            snapshot = FeatureStateSnapshot.Deserialize(message.StateVersion, message.StateJson);
            if (snapshot == null)
            {
                // Unreadable or from a newer version: refuse, so the Host keeps its widget.
                return new HandoffMessage { Type = HandoffTypes.Adopted, Accepted = false, Reason = "state" };
            }
        }

        await _surface.RunOnUiAsync(() =>
        {
            if (snapshot != null) _viewModel.AdoptFeatureState(snapshot);
            else if (chipsOnly != null) _viewModel.AdoptChipsSettings(chipsOnly);
            _surface.ShowAt(message.X, message.Y, message.Width, message.Height, message.Dpi);
        }).ConfigureAwait(false);

        return new HandoffMessage
        {
            Type = HandoffTypes.Adopted,
            Accepted = true,
            Reason = snapshot == null ? "state-kept" : null
        };
    }

    // Tells the Host where the widget is being dragged (physical pixels); the Host shows its insertion marker.
    public async Task SendDockHoverAsync(double cursorX, double cursorY, bool over)
    {
        var channel = _listener.Current;
        if (channel == null) return;
        try
        {
            await channel.SendAsync(new HandoffMessage
            {
                Type = over ? HandoffTypes.DockHover : HandoffTypes.DockHoverEnd,
                CursorX = cursorX,
                CursorY = cursorY
            }).ConfigureAwait(false);
        }
        catch (HandoffException)
        {
        }
    }

    // Hands ownership back. Call on the UI thread. True once the Host has taken over; the caller then stops
    // collecting and closes its window (and normally exits). False leaves everything as it was.
    public async Task<bool> RequestDockAsync(double cursorX, double cursorY)
    {
        var channel = _listener.Current;
        if (channel == null) return false;

        var snapshot = _viewModel.CaptureFeatureState();
        try
        {
            var reply = await channel.RequestAsync(new HandoffMessage
            {
                Type = HandoffTypes.DockRequest,
                StateVersion = FeatureStateSnapshot.CurrentVersion,
                StateJson = snapshot.Serialize(),
                CursorX = cursorX,
                CursorY = cursorY
            }, _ackTimeout).ConfigureAwait(true);
            return reply.Type == HandoffTypes.Docked && reply.Accepted;
        }
        catch (HandoffException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync() => _listener.DisposeAsync();
}
