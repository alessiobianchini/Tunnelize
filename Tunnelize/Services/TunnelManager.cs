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
}