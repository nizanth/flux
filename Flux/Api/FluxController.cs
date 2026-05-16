using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Flux.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Api;

/// <summary>Flux plugin API endpoints, proxying calls that the browser cannot make directly due to CORS.</summary>
[ApiController]
[Route("Flux")]
public class FluxController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FluxController> _logger;

    /// <summary>Initializes a new instance of <see cref="FluxController"/>.</summary>
    public FluxController(IHttpClientFactory httpClientFactory, ILogger<FluxController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Tests Xtream Codes credentials server-side to avoid browser CORS restrictions.</summary>
    /// <param name="url">Provider base URL.</param>
    /// <param name="username">Xtream Codes username.</param>
    /// <param name="password">Xtream Codes password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("TestConnection")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<TestConnectionResult>> TestConnection(
        [FromQuery, Required] string url,
        [FromQuery, Required] string username,
        [FromQuery, Required] string password,
        CancellationToken cancellationToken)
    {
        var baseUrl = url.TrimEnd('/');
        var apiUrl = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";

        try
        {
            var client = _httpClientFactory.CreateClient("Flux");
            var auth = await client.GetFromJsonAsync<AuthResponse>(apiUrl, cancellationToken).ConfigureAwait(false);

            if (auth?.UserInfo is null)
            {
                return Ok(new TestConnectionResult { Success = false, Message = "Invalid response from provider" });
            }

            if (auth.UserInfo.Auth != 1)
            {
                return Ok(new TestConnectionResult { Success = false, Message = "Authentication failed — check credentials or account status" });
            }

            var exp = long.TryParse(auth.UserInfo.ExpDate, out var ts)
                ? DateTimeOffset.FromUnixTimeSeconds(ts).ToString("yyyy-MM-dd")
                : "N/A";

            return Ok(new TestConnectionResult
            {
                Success = true,
                Message = $"Connected — Status: {auth.UserInfo.Status ?? "?"} | Expires: {exp} | Max connections: {auth.UserInfo.MaxConnections}"
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Flux: test connection failed for {Url}", baseUrl);
            return Ok(new TestConnectionResult { Success = false, Message = $"Cannot reach provider: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flux: test connection error");
            return Ok(new TestConnectionResult { Success = false, Message = $"Error: {ex.Message}" });
        }
    }

    /// <summary>Result model for TestConnection.</summary>
    public sealed class TestConnectionResult
    {
        /// <summary>Gets or sets a value indicating whether the connection succeeded.</summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>Gets or sets the human-readable result message.</summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}
