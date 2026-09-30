using System.IO;
using System.Diagnostics;
using AIUsage.Core;
using AIUsage.Core.Handoff;
using Dora.Widget.Abstractions;
using Microsoft.Win32;

namespace AIUsage.Widget;

// Host side of the ownership hand-over with the original AI Usage app. When the widget is dragged out of the
// Host, this gives the app the widget's state and position; when the app's mini widget is dropped onto the Host,
// it raises DockRequested. It talks to the app over a named pipe and never touches the app's windows.
public sealed class AIUsageDetachHandler : IWidgetDetachHandler
{
    // How long the app gets to start listening, and to apply the state and show itself.
    public static readonly TimeSpan DefaultLaunchTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DefaultAckTimeout = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;
    private readonly Func<bool> _launchApp;
    private readonly TimeSpan _launchTimeout;
    private readonly TimeSpan _ackTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HandoffChannel? _channel;
    private bool _detached;

    // The Host's plugin loader needs a truly parameterless constructor.
    public AIUsageDetachHandler() : this(HandoffPipe.DefaultName, LaunchRegisteredApp, DefaultLaunchTimeout, DefaultAckTimeout)
    {
    }

    // launchApp: starts the app for a hand-over; returns false when it cannot be started. Injectable for tests.
    public AIUsageDetachHandler(string pipeName, Func<bool> launchApp, TimeSpan launchTimeout, TimeSpan ackTimeout)
    {
        _pipeName = pipeName;
        _launchApp = launchApp;
        _launchTimeout = launchTimeout;
        _ackTimeout = ackTimeout;
    }

    public string WidgetId => AIUsageWidgetManifest.WidgetId;

    public bool IsDetached => _detached;

    public event Func<DockRequest, Task<bool>>? DockRequested;
    public event Action<WidgetPoint?>? DockHover;
    public event Action? DetachEnded;

    public async Task<bool> DetachAsync(DetachRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            if (channel == null) return false;

            HandoffMessage reply;
            try
            {
                reply = await channel.RequestAsync(new HandoffMessage
                {
                    Type = HandoffTypes.Adopt,
                    StateVersion = request.StateVersion,
                    StateJson = request.StateJson,
                    X = request.ScreenBounds.X,
                    Y = request.ScreenBounds.Y,
                    Width = request.ScreenBounds.Width,
                    Height = request.ScreenBounds.Height,
                    Dpi = request.Dpi
                }, _ackTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (HandoffException ex)
            {
                AppLog.Write("detach: no acknowledgement from the app: " + ex.Message);
                await TryRevokeAsync(channel).ConfigureAwait(false);
                return false;
            }

            if (reply.Type != HandoffTypes.Adopted || !reply.Accepted)
            {
                AppLog.Write("detach: the app refused the hand-over (" + reply.Reason + ")");
                return false;
            }
            _detached = true;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Connects to an app that was started on its own (never launches one), so its mini widget can be dropped onto
    // the Host without a prior drag-out. Called when the Host starts and whenever an app announces itself.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return; // a hand-over is in progress
        try
        {
            await ConnectAsync(cancellationToken, launch: false).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Connects to the running app, starting it first when nobody is listening. Null when that does not work out.
    private async Task<HandoffChannel?> ConnectAsync(CancellationToken ct, bool launch = true)
    {
        if (_channel is { IsClosed: false } existing) return existing;

        var channel = await TryConnectAsync(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
        if (channel == null)
        {
            if (!launch || !_launchApp()) return null;
            var deadline = DateTime.UtcNow + _launchTimeout;
            while (channel == null && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                channel = await TryConnectAsync(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
                if (channel == null) await Task.Delay(150, ct).ConfigureAwait(false);
            }
            if (channel == null) return null;
        }

        try
        {
            var welcome = await channel.RequestAsync(new HandoffMessage
            {
                Type = HandoffTypes.Hello,
                Protocol = HandoffTypes.ProtocolVersion,
                Pid = Environment.ProcessId
            }, _ackTimeout, ct).ConfigureAwait(false);
            if (welcome.Type != HandoffTypes.Welcome || !welcome.Accepted)
            {
                AppLog.Write("detach: the app speaks another protocol (" + welcome.Reason + ")");
                await channel.DisposeAsync().ConfigureAwait(false);
                return null;
            }
        }
        catch (HandoffException ex)
        {
            AppLog.Write("detach: handshake failed: " + ex.Message);
            await channel.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        channel.Closed += OnChannelClosed;
        _channel = channel;
        return channel;
    }

    private async Task<HandoffChannel?> TryConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            return await HandoffPipe.ConnectAsync(_pipeName, timeout, OnRequestAsync, ct).ConfigureAwait(false);
        }
        catch (HandoffException)
        {
            return null;
        }
    }

    // Messages the app starts: hover updates while its widget is dragged over the Host, and the final dock request.
    private async Task<HandoffMessage?> OnRequestAsync(HandoffMessage message)
    {
        switch (message.Type)
        {
            case HandoffTypes.DockHover:
                DockHover?.Invoke(new WidgetPoint(message.CursorX, message.CursorY));
                return null;

            case HandoffTypes.DockHoverEnd:
                DockHover?.Invoke(null);
                return null;

            case HandoffTypes.DockRequest:
                var accepted = false;
                var handler = DockRequested;
                if (handler != null)
                {
                    try
                    {
                        // The widget is created inside this call while the app still runs; let it through.
                        using var arrival = DockArrival.Begin();
                        accepted = await handler(new DockRequest(message.StateVersion, message.StateJson ?? "",
                            new WidgetPoint(message.CursorX, message.CursorY))).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write("dock request failed: " + ex.Message);
                    }
                }
                if (accepted) _detached = false; // ownership is back with the Host; the app hides its widget
                return new HandoffMessage { Type = HandoffTypes.Docked, Accepted = accepted, Reason = accepted ? null : "declined" };

            default:
                return null;
        }
    }

    private void OnChannelClosed()
    {
        var wasDetached = _detached;
        _detached = false;
        _channel = null;
        // The app went away without docking (closed by the user or crashed): there is nothing to show any more.
        if (wasDetached) DetachEnded?.Invoke();
    }

    private static async Task TryRevokeAsync(HandoffChannel channel)
    {
        try
        {
            await channel.SendAsync(new HandoffMessage { Type = HandoffTypes.Revoke }).ConfigureAwait(false);
        }
        catch (HandoffException)
        {
        }
    }

    // Starts the app from the path it registered itself under; nothing next to the widget DLL is trusted.
    private static bool LaunchRegisteredApp()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\AIUsageMonitor");
            var path = key?.GetValue("InstallPath") as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
                !string.Equals(Path.GetFileName(path), "AIUsageMonitor.exe", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Write("detach: the AI Usage app is not installed (no registered path)");
                return false;
            }
            Process.Start(new ProcessStartInfo(path, "--adopt") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("detach: could not start the app: " + ex.Message);
            return false;
        }
    }

    public async Task ShutdownAsync()
    {
        var channel = _channel;
        _channel = null;
        _detached = false;
        if (channel == null) return;
        channel.Closed -= OnChannelClosed;
        await channel.DisposeAsync().ConfigureAwait(false);
    }
}
