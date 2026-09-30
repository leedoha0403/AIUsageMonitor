using System.IO.Pipes;

namespace AIUsage.Core.Handoff;

// Named-pipe plumbing. The app listens (it can be running before any Host exists); a Host handler connects.
// CurrentUserOnly keeps other accounts on the machine out of the pipe.
public static class HandoffPipe
{
    public const string DefaultName = "AIUsage.Handoff.v1";

    // Accepts connections one after another; a newer connection replaces the previous one.
    public sealed class Listener : IAsyncDisposable
    {
        private readonly string _name;
        private readonly Func<HandoffMessage, Task<HandoffMessage?>> _onRequest;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;
        private HandoffChannel? _current;

        public Listener(string name, Func<HandoffMessage, Task<HandoffMessage?>> onRequest)
        {
            _name = name;
            _onRequest = onRequest;
        }

        // The channel of the currently connected Host, or null.
        public HandoffChannel? Current => _current is { IsClosed: false } c ? c : null;

        public event Action<HandoffChannel>? Connected;

        public void Start() => _loop ??= Task.Run(AcceptLoopAsync);

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    var channel = new HandoffChannel(server, _onRequest);
                    var previous = Interlocked.Exchange(ref _current, channel);
                    if (previous != null) await previous.DisposeAsync().ConfigureAwait(false);
                    channel.Start();
                    Connected?.Invoke(channel);
                }
                catch (OperationCanceledException)
                {
                    server?.Dispose();
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    server?.Dispose();
                    AppLog.Write("handoff listener: " + ex.Message);
                    try
                    {
                        await Task.Delay(500, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            if (_current != null) await _current.DisposeAsync().ConfigureAwait(false);
            if (_loop != null)
            {
                try
                {
                    await _loop.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    // Connects to a listening app; throws HandoffException when nobody answers within the timeout.
    public static async Task<HandoffChannel> ConnectAsync(string name, TimeSpan timeout, Func<HandoffMessage, Task<HandoffMessage?>>? onRequest = null, CancellationToken ct = default)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            client.Dispose();
            throw new HandoffException("No AI Usage app is listening.", ex);
        }
        var channel = new HandoffChannel(client, onRequest);
        channel.Start();
        return channel;
    }
}
