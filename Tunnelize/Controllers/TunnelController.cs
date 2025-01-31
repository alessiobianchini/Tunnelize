using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using System.Xml.Linq;

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

        var requestData = new
        {
            Method = method,
            QueryString = queryString.ToString(),
            Body = requestBody,
            Headers = headers,
            Route = $"/{routes}"
        };

        var message = System.Text.Json.JsonSerializer.Serialize(requestData);

        try
        {
            var wsResponse = await _tunnelManager.ForwardRequestToWSClient(tunnelId, message);

            if (string.IsNullOrWhiteSpace(wsResponse))
            {
                return StatusCode(502, new { message = "No response received from WebSocket client." });
            }

            var responseModel = System.Text.Json.JsonSerializer.Deserialize<ResponseModel>(wsResponse);

            if (responseModel != null)
            {
                Response.StatusCode = responseModel.StatusCode;
                Response.Headers.TryAdd("Content-Type", responseModel.ContentType ?? "application/json");
                return Content(responseModel.Body, HttpContext.Response.ContentType);
            }

            return StatusCode(500, new { message = "Error forwarding request" });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, new { message = "Gateway Timeout", error = "Timeout while waiting for response from WebSocket client." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error forwarding request", error = ex.Message });
        }
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

    public class ResponseModel
    {
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }
        [JsonPropertyName("body")]
        public string Body { get; set; }
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }
    }
}
