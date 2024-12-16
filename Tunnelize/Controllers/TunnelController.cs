using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("")]
public class TunnelController : ControllerBase
{
    private readonly TunnelManager _tunnelManager;
    
    public TunnelController(TunnelManager tunnelManager)
    {
        _tunnelManager = tunnelManager;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new { status = "Tunnelize active!" });
    }

    [HttpPost("create")]
    public IActionResult CreateTunnel([FromQuery] string tunnelId)
    {
        if (string.IsNullOrEmpty(tunnelId))
            return BadRequest("TunnelId is mandatory");

        return Ok(new { message = "Tunnel created!", tunnelId });
    }

    [HttpDelete("{tunnelId}")]
    public IActionResult CloseTunnel(string tunnelId)
    {
        return Ok(new { message = "Tunnel closed!", tunnelId });
    }

    [HttpGet("{tunnelId}/{*routes}")]
    [HttpPost("{tunnelId}/{*routes}")]
    public async Task<IActionResult> ProxyRequest(string tunnelId, string routes)
    {
        var method = HttpContext.Request.Method;
        var queryString = HttpContext.Request.QueryString;
        
        string requestBody = string.Empty;
        if (method == "POST")
        {
            using var reader = new StreamReader(HttpContext.Request.Body);
            requestBody = await reader.ReadToEndAsync();
        }

        var headers = GetAllHeaders(HttpContext.Request.Headers);
        var authHeader = HttpContext.Request.Headers["Authorization"].ToString();

        var requestData = new
        {
            Method = method,
            QueryString = queryString.ToString(),
            Body = requestBody,
            Authorization = authHeader,
            Headers = headers,
            Route = $"/{routes}"
        };

        var message = System.Text.Json.JsonSerializer.Serialize(requestData);

        await _tunnelManager.ForwardRequestToClient(tunnelId, message);

        return Ok(new { message = "Request forwarded to the local client" });
    }

    private Dictionary<string, string> GetAllHeaders(IHeaderDictionary headers)
    {
        Dictionary<string, string> requestHeaders = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            requestHeaders.Add(header.Key, header.Value);
        }

        return requestHeaders;
    }
}
