using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<TunnelManager>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.UseCors("AllowAll");

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/ws"))
    {
        await next();
        return;
    }

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected a WebSocket request.");
        return;
    }

    var tunnelManager = context.RequestServices.GetRequiredService<TunnelManager>();
    var requestedTunnelId = GetTunnelId(context.Request.Path.Value);
    var tunnelId = IsValidTunnelId(requestedTunnelId) ? requestedTunnelId! : GenerateRandomTunnelId();

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var buffer = Encoding.UTF8.GetBytes(tunnelId);
    await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, context.RequestAborted);

    await tunnelManager.HandleTunnelConnection(tunnelId, webSocket);
});

app.MapControllers();

app.Run();

static string? GetTunnelId(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return null;
    }

    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    if (segments.Length >= 2)
    {
        return segments[1];
    }

    return null;
}

static bool IsValidTunnelId(string? tunnelId)
{
    if (string.IsNullOrWhiteSpace(tunnelId) || tunnelId.Length != 10)
    {
        return false;
    }

    foreach (var c in tunnelId)
    {
        if (!char.IsLetterOrDigit(c))
        {
            return false;
        }
    }

    return true;
}

static string GenerateRandomTunnelId()
{
    const string AllowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    return RandomNumberGenerator.GetString(AllowedChars, 10);
}
