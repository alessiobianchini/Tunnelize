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
        var buffer = Encoding.UTF8.GetBytes(message);

        await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

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