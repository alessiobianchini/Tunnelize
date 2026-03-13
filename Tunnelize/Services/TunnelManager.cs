using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

public class TunnelManager
{
    private readonly ConcurrentDictionary<string, TunnelSession> _tunnels = new();
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeepAliveCheckInterval = TimeSpan.FromSeconds(30);

    public async Task HandleTunnelConnection(string tunnelId, WebSocket webSocket)
    {
        Console.WriteLine($"[INFO] Connection established for tunnel: {tunnelId}");

        var session = new TunnelSession(tunnelId, webSocket);

        if (_tunnels.TryGetValue(tunnelId, out var existing))
        {
            await existing.CloseAsync("Replaced by a new client connection");
            _tunnels.TryRemove(new KeyValuePair<string, TunnelSession>(tunnelId, existing));
        }

        if (!_tunnels.TryAdd(tunnelId, session))
        {
            await session.CloseAsync("Unable to register tunnel session");
            throw new InvalidOperationException($"Unable to register tunnel {tunnelId}.");
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(session.LifetimeToken);
            var receiveTask = ReceiveLoopAsync(session, linkedCts.Token);
            var keepAliveTask = KeepAliveLoopAsync(session, linkedCts.Token);

            await receiveTask;
            linkedCts.Cancel();

            try
            {
                await keepAliveTask;
            }
            catch (OperationCanceledException)
            {
                // expected during shutdown
            }
        }
        finally
        {
            if (_tunnels.TryGetValue(tunnelId, out var current) && ReferenceEquals(current, session))
            {
                _tunnels.TryRemove(new KeyValuePair<string, TunnelSession>(tunnelId, session));
            }

            await session.CloseAsync("Tunnel connection closed");
            Console.WriteLine($"[INFO] Tunnel {tunnelId} removed.");
        }
    }

    public async Task<bool> TryCloseTunnelAsync(string tunnelId, string reason = "Tunnel closed by API request")
    {
        if (!_tunnels.TryGetValue(tunnelId, out var session))
        {
            return false;
        }

        _tunnels.TryRemove(new KeyValuePair<string, TunnelSession>(tunnelId, session));
        await session.CloseAsync(reason);
        Console.WriteLine($"[INFO] Tunnel {tunnelId} closed by request.");
        return true;
    }

    public async Task<string> ForwardRequestToWSClient(string tunnelId, string message, CancellationToken cancellationToken = default)
    {
        if (!_tunnels.TryGetValue(tunnelId, out var session))
        {
            throw new KeyNotFoundException($"Tunnel {tunnelId} not found.");
        }

        await session.RequestLock.WaitAsync(cancellationToken);

        try
        {
            if (session.WebSocket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException($"Tunnel {tunnelId} is not connected.");
            }

            var buffer = Encoding.UTF8.GetBytes(message);
            await session.WebSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
            session.MarkActivity();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.LifetimeToken);
            timeoutCts.CancelAfter(RequestTimeout);

            try
            {
                var response = await session.IncomingMessages.Reader.ReadAsync(timeoutCts.Token);
                session.MarkActivity();
                return response;
            }
            catch (ChannelClosedException)
            {
                throw new InvalidOperationException($"Tunnel {tunnelId} was closed while waiting for response.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !session.LifetimeToken.IsCancellationRequested)
            {
                throw new TimeoutException("Timeout while waiting for response from WebSocket client.");
            }
        }
        finally
        {
            session.RequestLock.Release();
        }
    }

    private static async Task ReceiveLoopAsync(TunnelSession session, CancellationToken cancellationToken)
    {
        var receiveBuffer = new byte[64 * 1024];
        var messageBuffer = new ArrayBufferWriter<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested && session.WebSocket.State == WebSocketState.Open)
            {
                var result = await session.WebSocket.ReceiveAsync(receiveBuffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"[INFO] Client closed tunnel {session.TunnelId}.");
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                messageBuffer.Write(receiveBuffer.AsSpan(0, result.Count));

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var message = Encoding.UTF8.GetString(messageBuffer.WrittenSpan);
                messageBuffer.Clear();

                session.MarkActivity();

                if (string.Equals(message, "pong", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Pong received for tunnel {session.TunnelId}.");
                    continue;
                }

                await session.IncomingMessages.Writer.WriteAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected during shutdown
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"[ERROR] Receive loop failed for tunnel {session.TunnelId}: {ex.Message}");
        }
        finally
        {
            session.IncomingMessages.Writer.TryComplete();
        }
    }

    private static async Task KeepAliveLoopAsync(TunnelSession session, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && session.WebSocket.State == WebSocketState.Open)
            {
                await Task.Delay(KeepAliveCheckInterval, cancellationToken);

                if (session.GetInactivityDuration() < KeepAliveInterval)
                {
                    continue;
                }

                await session.WebSocket.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, cancellationToken);
                Console.WriteLine($"[DEBUG] Sent ping to tunnel {session.TunnelId}.");
            }
        }
        catch (OperationCanceledException)
        {
            // expected during shutdown
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"[ERROR] Ping failed for tunnel {session.TunnelId}: {ex.Message}");
        }
    }

    private sealed class TunnelSession
    {
        private int _isClosed;
        private long _lastActivityTicks;

        public TunnelSession(string tunnelId, WebSocket webSocket)
        {
            TunnelId = tunnelId;
            WebSocket = webSocket;
            RequestLock = new SemaphoreSlim(1, 1);
            IncomingMessages = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true
            });
            LifetimeCts = new CancellationTokenSource();
            _lastActivityTicks = DateTime.UtcNow.Ticks;
        }

        public string TunnelId { get; }
        public WebSocket WebSocket { get; }
        public SemaphoreSlim RequestLock { get; }
        public Channel<string> IncomingMessages { get; }
        private CancellationTokenSource LifetimeCts { get; }

        public CancellationToken LifetimeToken => LifetimeCts.Token;

        public void MarkActivity()
        {
            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        }

        public TimeSpan GetInactivityDuration()
        {
            var lastActivity = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
            return DateTime.UtcNow - lastActivity;
        }

        public async Task CloseAsync(string reason)
        {
            if (Interlocked.Exchange(ref _isClosed, 1) != 0)
            {
                return;
            }

            if (!LifetimeCts.IsCancellationRequested)
            {
                LifetimeCts.Cancel();
            }

            IncomingMessages.Writer.TryComplete();

            if (WebSocket.State == WebSocketState.Open || WebSocket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
                }
                catch
                {
                    // ignore close failures during cleanup
                }
            }

            LifetimeCts.Dispose();
        }
    }
}
