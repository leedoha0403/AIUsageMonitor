using AIUsage.Core.Handoff;

namespace AIUsage.Tests;

// The pipe protocol on real named pipes (unique names per test).
public class HandoffChannelTests
{
    private static string NewName() => "AIUsageTest.Handoff." + Guid.NewGuid().ToString("N");

    private static HandoffMessage Reply(string type, bool accepted = true, string? reason = null) =>
        new() { Type = type, Accepted = accepted, Reason = reason };

    private static readonly TimeSpan Short = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Request_gets_the_matching_reply()
    {
        var name = NewName();
        await using var listener = new HandoffPipe.Listener(name, m => Task.FromResult<HandoffMessage?>(
            m.Type == HandoffTypes.Hello ? new HandoffMessage { Type = HandoffTypes.Welcome, Protocol = HandoffTypes.ProtocolVersion, AppVersion = "9.9.9", Accepted = true } : null));
        listener.Start();

        await using var client = await HandoffPipe.ConnectAsync(name, Short);
        var reply = await client.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello, Protocol = 1, Pid = 42 }, Short);

        Assert.Equal(HandoffTypes.Welcome, reply.Type);
        Assert.Equal("9.9.9", reply.AppVersion);
        Assert.True(reply.Accepted);
    }

    [Fact]
    public async Task State_payload_survives_the_roundtrip_intact()
    {
        var name = NewName();
        var big = new string('x', 300_000) + "\n\"quotes\" and unicode: 한글";
        HandoffMessage? seen = null;
        await using var listener = new HandoffPipe.Listener(name, m =>
        {
            seen = m;
            return Task.FromResult<HandoffMessage?>(Reply(HandoffTypes.Adopted));
        });
        listener.Start();

        await using var client = await HandoffPipe.ConnectAsync(name, Short);
        await client.RequestAsync(new HandoffMessage { Type = HandoffTypes.Adopt, StateVersion = 3, StateJson = big, X = 1234.5, Y = -20, Dpi = 144 }, Short);

        Assert.Equal(big, seen!.StateJson);
        Assert.Equal(3, seen.StateVersion);
        Assert.Equal(1234.5, seen.X);
        Assert.Equal(-20, seen.Y);
        Assert.Equal(144, seen.Dpi);
    }

    [Fact]
    public async Task Either_side_can_start_a_request()
    {
        var name = NewName();
        await using var listener = new HandoffPipe.Listener(name, m => Task.FromResult<HandoffMessage?>(null));
        var connected = new TaskCompletionSource<HandoffChannel>();
        listener.Connected += c => connected.TrySetResult(c);
        listener.Start();

        await using var client = await HandoffPipe.ConnectAsync(name, Short, m =>
            Task.FromResult<HandoffMessage?>(m.Type == HandoffTypes.DockRequest ? Reply(HandoffTypes.Docked) : null));
        var server = await connected.Task.WaitAsync(Short);

        var ack = await server.RequestAsync(new HandoffMessage { Type = HandoffTypes.DockRequest, CursorX = 10, CursorY = 20 }, Short);
        Assert.Equal(HandoffTypes.Docked, ack.Type);
    }

    [Fact]
    public async Task Unanswered_request_times_out_with_a_clear_error()
    {
        var name = NewName();
        await using var listener = new HandoffPipe.Listener(name, m => Task.FromResult<HandoffMessage?>(null));
        listener.Start();
        await using var client = await HandoffPipe.ConnectAsync(name, Short);

        var ex = await Assert.ThrowsAsync<HandoffException>(() =>
            client.RequestAsync(new HandoffMessage { Type = HandoffTypes.Adopt }, TimeSpan.FromMilliseconds(300)));
        Assert.Contains("No reply", ex.Message);
    }

    [Fact]
    public async Task Peer_disconnect_fails_pending_requests_and_raises_closed()
    {
        var name = NewName();
        var gate = new TaskCompletionSource<bool>();
        HandoffChannel? serverSide = null;
        await using var listener = new HandoffPipe.Listener(name, async m =>
        {
            await gate.Task;
            return null;
        });
        listener.Connected += c => serverSide = c;
        listener.Start();

        await using var client = await HandoffPipe.ConnectAsync(name, Short);
        var closed = new TaskCompletionSource<bool>();
        client.Closed += () => closed.TrySetResult(true);

        var pending = client.RequestAsync(new HandoffMessage { Type = HandoffTypes.Adopt }, Short);
        await Task.Delay(100);
        await serverSide!.DisposeAsync();

        await Assert.ThrowsAsync<HandoffException>(() => pending);
        await closed.Task.WaitAsync(Short);
        Assert.True(client.IsClosed);
        gate.TrySetResult(true);
    }

    [Fact]
    public async Task Connecting_without_a_listener_fails_fast()
    {
        await Assert.ThrowsAsync<HandoffException>(() => HandoffPipe.ConnectAsync(NewName(), TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task A_new_connection_replaces_the_previous_one()
    {
        var name = NewName();
        await using var listener = new HandoffPipe.Listener(name, m => Task.FromResult<HandoffMessage?>(Reply("ok")));
        listener.Start();

        await using var first = await HandoffPipe.ConnectAsync(name, Short);
        await first.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello }, Short);
        await using var second = await HandoffPipe.ConnectAsync(name, Short);
        await second.RequestAsync(new HandoffMessage { Type = HandoffTypes.Hello }, Short);

        // The first Host was dropped in favor of the newer one.
        var deadline = DateTime.UtcNow + Short;
        while (!first.IsClosed && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(first.IsClosed);
        Assert.False(second.IsClosed);
        Assert.NotNull(listener.Current);
    }

    [Fact]
    public async Task A_garbled_line_is_ignored_and_the_channel_keeps_working()
    {
        // Feed the channel raw text through a duplex memory stream pair.
        var (a, b) = DuplexStream.Create();
        var received = new TaskCompletionSource<HandoffMessage>();
        await using var channel = new HandoffChannel(a, m =>
        {
            received.TrySetResult(m);
            return Task.FromResult<HandoffMessage?>(null);
        });
        channel.Start();

        var junk = System.Text.Encoding.UTF8.GetBytes("this is not json\n{\"type\":\"hello\",\"protocol\":1}\n");
        await b.WriteAsync(junk);
        await b.FlushAsync();

        var message = await received.Task.WaitAsync(Short);
        Assert.Equal(HandoffTypes.Hello, message.Type);
        Assert.False(channel.IsClosed);
    }
}

// Two connected in-memory streams (what one writes, the other reads).
internal static class DuplexStream
{
    public static (Stream A, Stream B) Create()
    {
        var aToB = new BlockingPipe();
        var bToA = new BlockingPipe();
        return (new Half(bToA, aToB), new Half(aToB, bToA));
    }

    private sealed class BlockingPipe
    {
        private readonly System.Threading.Channels.Channel<byte[]> _channel = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
        private byte[] _current = [];
        private int _offset;

        public void Write(byte[] data) => _channel.Writer.TryWrite(data);
        public void Complete() => _channel.Writer.TryComplete();

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            if (_offset >= _current.Length)
            {
                try
                {
                    _current = await _channel.Reader.ReadAsync(ct);
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    return 0;
                }
                _offset = 0;
            }
            var n = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, n).CopyTo(buffer);
            _offset += n;
            return n;
        }
    }

    private sealed class Half : Stream
    {
        private readonly BlockingPipe _in;
        private readonly BlockingPipe _out;

        public Half(BlockingPipe input, BlockingPipe output)
        {
            _in = input;
            _out = output;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _in.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _out.Write(buffer.AsSpan(offset, count).ToArray());
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _out.Write(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _out.Complete();
            base.Dispose(disposing);
        }
    }
}
