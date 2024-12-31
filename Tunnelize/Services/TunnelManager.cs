using System;
using System.Net.WebSockets;
using System.Text;

public class TunnelManager
{
    private readonly Dictionary<string, WebSocket> _tunnels = new();
    private readonly Dictionary<string, DateTime> _lastActivityTracker = new(); // Track last activity per tunnel

    public async Task HandleTunnelConnection(string tunnelId, WebSocket webSocket)
    {
        Console.WriteLine($"[INFO] Connection established for tunnel: {tunnelId}");

        if (!_tunnels.ContainsKey(tunnelId))
        {
            _tunnels[tunnelId] = webSocket;
            _lastActivityTracker[tunnelId] = DateTime.UtcNow;
        }

        var buffer = new byte[1024 * 1024]; 
        var cts = new CancellationTokenSource();
        var keepAliveInterval = TimeSpan.FromMinutes(20);

        _ = Task.Run(async () =>
        {
            while (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    if (_lastActivityTracker.ContainsKey(tunnelId) && DateTime.UtcNow - _lastActivityTracker[tunnelId] >= keepAliveInterval)
                    {
                        Console.WriteLine($"[DEBUG] No activity for 5 minutes. Sending ping to client for tunnel {tunnelId}");
                        await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("ping")), WebSocketMessageType.Text, true, CancellationToken.None);

                        try
                        {
                            while (true)
                            {
                                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                                if (result.MessageType == WebSocketMessageType.Close)
                                {
                                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed connection", CancellationToken.None);
                                    throw new Exception($"WebSocket connection closed by client for tunnel {tunnelId}");
                                }

                                if (result.MessageType == WebSocketMessageType.Text)
                                {
                                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                                    if (message == "pong")
                                    {
                                        Console.WriteLine($"[DEBUG] Pong received from client for tunnel {tunnelId}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"[DEBUG] Received message for tunnel {tunnelId}: {message}");
                                    }

                                    if (_lastActivityTracker.ContainsKey(tunnelId))
                                    {
                                        _lastActivityTracker[tunnelId] = DateTime.UtcNow;
                                    }
                                }

                                if (result.EndOfMessage)
                                {
                                    break;
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw new Exception("Timeout while waiting for pong from WebSocket client.");
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token); 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Ping failed for tunnel {tunnelId}: {ex.Message}");
                    break;
                }
            }
        });

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Tunnel error {tunnelId}: {ex.Message}");
        }
        finally
        {
            cts.Cancel(); 

            if (_tunnels.ContainsKey(tunnelId))
            {
                _tunnels.Remove(tunnelId);
                Console.WriteLine($"[INFO] Tunnel {tunnelId} removed.");
            }

            if (_lastActivityTracker.ContainsKey(tunnelId))
            {
                _lastActivityTracker.Remove(tunnelId);
            }
        }
    }

    public async Task ForwardRequestToClient(string tunnelId, string message)
    {
        if (!_tunnels.ContainsKey(tunnelId))
        {
            throw new Exception($"Tunnel {tunnelId} not found.");
        }

        var webSocket = _tunnels[tunnelId];
        var buffer = Encoding.UTF8.GetBytes(message);

        await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

        if (_lastActivityTracker.ContainsKey(tunnelId))
        {
            _lastActivityTracker[tunnelId] = DateTime.UtcNow;
        }
    }

    public async Task<string> ForwardRequestToWSClient(string tunnelId, string message)
    {
        if (!_tunnels.ContainsKey(tunnelId))
        {
            throw new Exception($"Tunnel {tunnelId} not found.");
        }

        var webSocket = _tunnels[tunnelId];
        var buffer = Encoding.UTF8.GetBytes(message);

        await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

        if (_lastActivityTracker.ContainsKey(tunnelId))
        {
            _lastActivityTracker[tunnelId] = DateTime.UtcNow;
        }

        var responseBuffer = new byte[1024 * 1024 * 5];
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var completeResponse = new List<byte>();

        try
        {
            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(responseBuffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed connection", CancellationToken.None);
                    throw new Exception($"WebSocket connection closed by client for tunnel {tunnelId}");
                }

                completeResponse.AddRange(responseBuffer.Take(result.Count));

                if (result.EndOfMessage)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new Exception("Timeout while waiting for response from WebSocket client.");
        }

        var jsonResponse = Encoding.UTF8.GetString(completeResponse.ToArray());
        return jsonResponse;
    }
}
