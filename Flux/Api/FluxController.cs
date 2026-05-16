using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Flux.Api.Dto;
using Jellyfin.Plugin.Flux.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Api;

/// <summary>Flux plugin API endpoints used by the admin configuration page.</summary>
[ApiController]
[Route("Flux")]
public class FluxController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CatalogSyncService _sync;
    private readonly HealthMonitor _health;
    private readonly ProviderRegistry _registry;
    private readonly ILogger<FluxController> _logger;

    /// <summary>Initializes a new instance of <see cref="FluxController"/>.</summary>
    public FluxController(
        IHttpClientFactory httpClientFactory,
        CatalogSyncService sync,
        HealthMonitor health,
        ProviderRegistry registry,
        ILogger<FluxController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sync = sync;
        _health = health;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>Tests Xtream Codes credentials server-side to avoid browser CORS restrictions.</summary>
    [HttpGet("TestConnection")]
    [AllowAnonymous]
    public async Task<ActionResult<TestConnectionResult>> TestConnection(
        [FromQuery] string? url,
        [FromQuery] string? username,
        [FromQuery] string? password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(username))
        {
            return Ok(new TestConnectionResult { Success = false, Message = "URL and username are required" });
        }

        var baseUrl = url.TrimEnd('/');
        var apiUrl = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password ?? string.Empty)}";

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

    /// <summary>
    /// Triggers a catalog sync directly. Accepted types: live, vod, series, all.
    /// This avoids needing to look up Jellyfin's internally-computed scheduled task IDs.
    /// </summary>
    [HttpPost("Sync/{syncType}")]
    [AllowAnonymous]
    public ActionResult TriggerSync(string syncType)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                await _sync.SyncAllAsync(cts.Token, syncType.ToLowerInvariant()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flux: manual sync ({Type}) failed", syncType);
            }
        });

        return Ok(new { started = true });
    }

    /// <summary>Returns live health status for all configured providers.</summary>
    [HttpGet("Health")]
    [AllowAnonymous]
    public ActionResult<IEnumerable<ProviderHealthResult>> GetHealth()
    {
        var providers = _registry.GetAll();
        var statuses = _health.GetAllStatuses().ToDictionary(s => s.ProviderId);

        var results = providers.Select(p =>
        {
            statuses.TryGetValue(p.Id, out var status);
            return new ProviderHealthResult
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                Health = status?.Health.ToString() ?? "Unknown",
                LastError = status?.LastError,
                LastSuccessAt = status?.LastSuccessAt?.ToString("o")
            };
        });

        return Ok(results);
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

    /// <summary>Result model for GetHealth.</summary>
    public sealed class ProviderHealthResult
    {
        /// <summary>Gets or sets the provider GUID.</summary>
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        /// <summary>Gets or sets the provider display name.</summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Gets or sets the health status string.</summary>
        [JsonPropertyName("health")]
        public string Health { get; set; } = "Unknown";

        /// <summary>Gets or sets the last error message, if any.</summary>
        [JsonPropertyName("lastError")]
        public string? LastError { get; set; }

        /// <summary>Gets or sets the ISO-8601 timestamp of the last successful sync.</summary>
        [JsonPropertyName("lastSuccessAt")]
        public string? LastSuccessAt { get; set; }
    }
}
