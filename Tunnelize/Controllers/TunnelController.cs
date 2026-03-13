using System.Text.Json;
using System.Text.Json.Serialization;
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
    [HttpPost("tunnels")]
    public IActionResult CreateTunnel([FromQuery] string tunnelId)
    {
        if (string.IsNullOrWhiteSpace(tunnelId))
        {
            return BadRequest("TunnelId is mandatory");
        }

        return Ok(new { message = "Tunnel created!", tunnelId });
    }

    [HttpDelete("{tunnelId}")]
    [HttpDelete("tunnels/{tunnelId}")]
    public async Task<IActionResult> CloseTunnel(string tunnelId)
    {
        var closed = await _tunnelManager.TryCloseTunnelAsync(tunnelId, "Tunnel closed from API");

        if (!closed)
        {
            return NotFound(new { message = "Tunnel not found", tunnelId });
        }

        return Ok(new { message = "Tunnel closed!", tunnelId });
    }

    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD")]
    [Route("{tunnelId}/{*routePath}")]
    public async Task<IActionResult> ProxyRequest(string tunnelId, string? routePath)
    {
        var method = HttpContext.Request.Method;
        var queryString = HttpContext.Request.QueryString.ToString();
        var requestBody = await ReadRequestBodyAsync(HttpContext.Request);

        var requestData = new ProxyRequestModel
        {
            Method = method,
            QueryString = queryString,
            Body = requestBody,
            Headers = GetAllHeaders(HttpContext.Request.Headers),
            Route = $"/{routePath ?? string.Empty}"
        };

        var message = JsonSerializer.Serialize(requestData);

        try
        {
            var wsResponse = await _tunnelManager.ForwardRequestToWSClient(tunnelId, message, HttpContext.RequestAborted);

            if (string.IsNullOrWhiteSpace(wsResponse))
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "No response received from WebSocket client." });
            }

            var responseModel = JsonSerializer.Deserialize<ResponseModel>(wsResponse);

            if (responseModel is null)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Invalid response received from WebSocket client." });
            }

            return BuildProxyResponse(responseModel);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Tunnel not found", tunnelId });
        }
        catch (TimeoutException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { message = "Gateway timeout while waiting for WebSocket client response." });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499, new { message = "Client closed request." });
        }
        catch (JsonException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Malformed response from WebSocket client.", error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error forwarding request", error = ex.Message });
        }
    }

    private IActionResult BuildProxyResponse(ResponseModel responseModel)
    {
        Response.StatusCode = responseModel.StatusCode is >= 100 and <= 599
            ? responseModel.StatusCode
            : StatusCodes.Status502BadGateway;

        if (!string.IsNullOrWhiteSpace(responseModel.ContentType))
        {
            Response.ContentType = responseModel.ContentType;
        }
        else
        {
            Response.ContentType = "application/json";
        }

        if (responseModel.Headers is not null)
        {
            foreach (var header in responseModel.Headers)
            {
                if (string.Equals(header.Key, "content-type", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "content-length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Response.Headers[header.Key] = header.Value;
            }
        }

        if (HttpMethods.IsHead(HttpContext.Request.Method))
        {
            return new EmptyResult();
        }

        return Content(responseModel.Body ?? string.Empty, Response.ContentType);
    }

    private static Dictionary<string, string> GetAllHeaders(IHeaderDictionary headers)
    {
        return headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        if (!CanHaveBody(request.Method) || request.ContentLength is 0)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }

    private static bool CanHaveBody(string method)
    {
        return HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);
    }

    private sealed class ProxyRequestModel
    {
        public string Method { get; set; } = string.Empty;
        public string QueryString { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Route { get; set; } = string.Empty;
    }

    public sealed class ResponseModel
    {
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("contentType")]
        public string? ContentType { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }
    }
}
