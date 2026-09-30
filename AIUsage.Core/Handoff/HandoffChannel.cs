using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AIUsage.Core.Handoff;

// Full-duplex JSON-lines channel over any stream (a named pipe in production, a memory pipe in tests).
// Either side can send requests; replies are matched by ReplyTo. Nothing here knows about the app.
public sealed class HandoffChannel : IAsyncDisposable
{
    // A state snapshot is small; anything near this size is not a legitimate peer.
    public const int MaxLineBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly Stream _stream;
    private readonly Func<HandoffMessage, Task<HandoffMessage?>>? _onRequest;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<HandoffMessage>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    // Incoming requests are handled one at a time, in arrival order (a hover end must never overtake its hover),
    // but off the read loop so a slow handler never delays replies to our own requests.
    private readonly System.Threading.Channels.Channel<HandoffMessage> _inbox =
        System.Threading.Channels.Channel.CreateUnbounded<HandoffMessage>(new() { SingleReader = true });
    private Task? _reader;
    private Task? _worker;
    private int _closed;

    public HandoffChannel(Stream stream, Func<HandoffMessage, Task<HandoffMessage?>>? onRequest = null)
    {
        _stream = stream;
        _onRequest = onRequest;
    }

    public event Action? Closed;

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public void Start()
    {
        if (_reader != null) return;
        _worker = Task.Run(HandleRequestsAsync);
        _reader = Task.Run(ReadLoopAsync);
    }

    public async Task SendAsync(HandoffMessage message, CancellationToken ct = default)
    {
        var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, Json) + "\n");
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(line, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Close();
            throw new HandoffException("The peer is gone.", ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // Sends a request and waits for its reply; throws HandoffException on timeout or a dropped connection.
    public async Task<HandoffMessage> RequestAsync(HandoffMessage request, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<HandoffMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = tcs;
        try
        {
            await SendAsync(request, ct).ConfigureAwait(false);
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timer.CancelAfter(timeout);
            await using var registration = timer.Token.Register(() => tcs.TrySetCanceled(timer.Token));
            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new HandoffException($"No reply to '{request.Type}' within {timeout.TotalSeconds:0.#}s.");
            }
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[8192];
        var line = new MemoryStream();
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                if (read == 0) break;
                for (var i = 0; i < read; i++)
                {
                    var b = buffer[i];
                    if (b != (byte)'\n')
                    {
                        if (line.Length >= MaxLineBytes) throw new HandoffException("Message too large.");
                        line.WriteByte(b);
                        continue;
                    }
                    var text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
                    line.SetLength(0);
                    if (text.Length > 0) Dispatch(text);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or HandoffException)
        {
            // fall through to Close
        }
        finally
        {
            Close();
        }
    }

    private void Dispatch(string text)
    {
        HandoffMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<HandoffMessage>(text, Json);
        }
        catch (JsonException)
        {
            return; // a garbled line from a misbehaving peer is dropped, not fatal
        }
        if (message == null || string.IsNullOrEmpty(message.Type)) return;

        if (message.ReplyTo != null)
        {
            if (_pending.TryGetValue(message.ReplyTo, out var tcs)) tcs.TrySetResult(message);
            return;
        }

        if (_onRequest == null) return;
        _inbox.Writer.TryWrite(message);
    }

    private async Task HandleRequestsAsync()
    {
        try
        {
            await foreach (var message in _inbox.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    var reply = await _onRequest!(message).ConfigureAwait(false);
                    if (reply == null) continue;
                    reply.ReplyTo = message.Id;
                    await SendAsync(reply).ConfigureAwait(false);
                }
                catch (HandoffException)
                {
                }
                catch (Exception ex)
                {
                    AppLog.Write($"handoff request '{message.Type}' failed: {ex}");
                    try
                    {
                        await SendAsync(new HandoffMessage { Type = message.Type + ".error", ReplyTo = message.Id, Accepted = false, Reason = "internal error" }).ConfigureAwait(false);
                    }
                    catch (HandoffException)
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _cts.Cancel();
        _inbox.Writer.TryComplete();
        foreach (var pending in _pending.Values) pending.TrySetException(new HandoffException("The connection closed."));
        try
        {
            _stream.Dispose();
        }
        catch (IOException)
        {
        }
        Closed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        Close();
        // Only the read loop is awaited. A request handler that is still running (it may be waiting on something
        // that never comes once the peer is gone) finishes on its own; disposing must not hang on it.
        if (_reader != null)
        {
            try
            {
                await _reader.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }
}

public sealed class HandoffException : Exception
{
    public HandoffException(string message) : base(message) { }
    public HandoffException(string message, Exception inner) : base(message, inner) { }
}
