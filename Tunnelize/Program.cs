using System.Net.WebSockets;
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

app.UseWebSockets();
app.UseCors("AllowAll");
app.MapControllers();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/ws"))
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            var tunnelManager = context.RequestServices.GetRequiredService<TunnelManager>();
            var tunnelId = GetTunnelId(context.Request.Path.Value);
            if (tunnelId == null)
            {
                tunnelId = GenerateRandomTunnelId();
            }
            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            byte[] buffer = Encoding.UTF8.GetBytes(tunnelId);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);

            await tunnelManager.HandleTunnelConnection(tunnelId, webSocket);
        }
        else
        {
            context.Response.StatusCode = 400;
        }
    }
    else
    {
        await next();
    }
});

app.Run();

static string GetTunnelId(string path)
{
    string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    if (segments.Length > 1)
    {
        return segments[1]; 
    }

    return null;
}

static string GenerateRandomTunnelId()
{
    const string AllowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    Random rnd = new Random();
    int count = 10;

    char[] chars = new char[10];
    int setLength = AllowedChars.Length;

    while (count-- > 0)
    {

        for (int i = 0; i < 10; ++i)
        {
            chars[i] = AllowedChars[rnd.Next(setLength)];
        }

    }

    string randomString = new string(chars, 0, 10);

    return randomString;
}