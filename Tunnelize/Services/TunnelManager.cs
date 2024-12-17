using System.Net.WebSockets;
using System.Text;

public class TunnelManager
{
    private readonly Dictionary<string, WebSocket> _tunnels = new();

    public async Task HandleTunnelConnection(string tunnelId, WebSocket webSocket)
    {
        Console.WriteLine($"[INFO] Connection established for tunnel: {tunnelId}");

        if (!_tunnels.ContainsKey(tunnelId))
        {
            _tunnels[tunnelId] = webSocket;
        }

        var buffer = new byte[1024 * 4];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"[INFO] Tunnel {tunnelId} closed.");
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closure", CancellationToken.None);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Tunnel error {tunnelId}: {ex.Message}");
        }
        finally
        {
            if (_tunnels.ContainsKey(tunnelId))
            {
                _tunnels.Remove(tunnelId);
                Console.WriteLine($"[INFO] Tunnel {tunnelId} removed.");
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
    }

    public async Task<string> ForwardRequestToWSClient(string tunnelId, string message)
    {
        if (!_tunnels.ContainsKey(tunnelId))
        {
            throw new Exception($"Tunnel {tunnelId} not found.");
        }

        var webSocket = _tunnels[tunnelId];
        var sendBuffer = Encoding.UTF8.GetBytes(message);

        await webSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, CancellationToken.None);

        var responseBuffer = new byte[1024 * 4];
        var receivedData = new StringBuilder();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            WebSocketReceiveResult result;

            do
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(responseBuffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed connection", CancellationToken.None);
                    throw new Exception($"WebSocket connection closed by client for tunnel {tunnelId}");
                }

                var chunk = Encoding.UTF8.GetString(responseBuffer, 0, result.Count);
                receivedData.Append(chunk);

            } while (!result.EndOfMessage);
        }
        catch (OperationCanceledException)
        {
            throw new Exception("Timeout while waiting for response from WebSocket client.");
        }

        return receivedData.ToString();
    }
}