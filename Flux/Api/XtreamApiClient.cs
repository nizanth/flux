using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Flux.Api.Dto;
using Jellyfin.Plugin.Flux.Configuration;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("Flux.Tests")]

namespace Jellyfin.Plugin.Flux.Api;

/// <summary>HTTP client for the Xtream Codes player_api.php and xmltv.php endpoints.</summary>
public sealed class XtreamApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private const int MaxAttempts = 5;
    private const int InitialBackoffMs = 2_000;
    private const double BackoffFactor = 2.0;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<XtreamApiClient> _logger;

    /// <summary>Initializes a new instance of <see cref="XtreamApiClient"/>.</summary>
    public XtreamApiClient(IHttpClientFactory httpClientFactory, ILogger<XtreamApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    /// <summary>Authenticates and retrieves user_info + server_info.</summary>
    public Task<AuthResponse?> AuthenticateAsync(ProviderConfig provider, CancellationToken ct = default)
        => GetJsonAsync<AuthResponse>(provider, string.Empty, ct);

    // ── Live ──────────────────────────────────────────────────────────────────

    /// <summary>Fetches all live categories.</summary>
    public Task<List<Category>?> GetLiveCategoriesAsync(ProviderConfig provider, CancellationToken ct = default)
        => GetJsonAsync<List<Category>>(provider, "&action=get_live_categories", ct);

    /// <summary>Fetches live streams, optionally filtered by category.</summary>
    public Task<List<LiveStream>?> GetLiveStreamsAsync(
        ProviderConfig provider, string? categoryId = null, CancellationToken ct = default)
    {
        var suffix = categoryId is null
            ? "&action=get_live_streams"
            : $"&action=get_live_streams&category_id={Uri.EscapeDataString(categoryId)}";
        return GetJsonAsync<List<LiveStream>>(provider, suffix, ct);
    }

    // ── EPG ───────────────────────────────────────────────────────────────────

    /// <summary>Downloads the full XMLTV EPG stream for a provider.</summary>
    public async Task<Stream?> GetXmltvStreamAsync(ProviderConfig provider, CancellationToken ct = default)
    {
        var url = BuildXmltvUrl(provider);
        _logger.LogDebug("Downloading XMLTV from {SafeUrl}", RedactUrl(url));

        var http = CreateClient(provider);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
            {
                var delay = (int)(InitialBackoffMs * Math.Pow(BackoffFactor, attempt - 1));
                _logger.LogWarning(ex, "XMLTV download attempt {Attempt}/{Max} failed; retrying in {Delay}ms",
                    attempt, MaxAttempts, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "XMLTV download failed after {Max} attempts", MaxAttempts);
                return null;
            }
        }

        return null;
    }

    /// <summary>Fetches short EPG entries for a single stream (fallback path).</summary>
    public Task<ShortEpgResponse?> GetShortEpgAsync(
        ProviderConfig provider, int streamId, int limit = 4, CancellationToken ct = default)
        => GetJsonAsync<ShortEpgResponse>(
            provider, $"&action=get_short_epg&stream_id={streamId}&limit={limit}", ct);

    // ── VOD ───────────────────────────────────────────────────────────────────

    /// <summary>Fetches all VOD categories.</summary>
    public Task<List<Category>?> GetVodCategoriesAsync(ProviderConfig provider, CancellationToken ct = default)
        => GetJsonAsync<List<Category>>(provider, "&action=get_vod_categories", ct);

    /// <summary>Fetches VOD streams, optionally filtered by category.</summary>
    public Task<List<VodStream>?> GetVodStreamsAsync(
        ProviderConfig provider, string? categoryId = null, CancellationToken ct = default)
    {
        var suffix = categoryId is null
            ? "&action=get_vod_streams"
            : $"&action=get_vod_streams&category_id={Uri.EscapeDataString(categoryId)}";
        return GetJsonAsync<List<VodStream>>(provider, suffix, ct);
    }

    /// <summary>Fetches detailed metadata for a single VOD item.</summary>
    public Task<VodInfo?> GetVodInfoAsync(ProviderConfig provider, int vodId, CancellationToken ct = default)
        => GetJsonAsync<VodInfo>(provider, $"&action=get_vod_info&vod_id={vodId}", ct);

    // ── Series ────────────────────────────────────────────────────────────────

    /// <summary>Fetches all series categories.</summary>
    public Task<List<Category>?> GetSeriesCategoriesAsync(ProviderConfig provider, CancellationToken ct = default)
        => GetJsonAsync<List<Category>>(provider, "&action=get_series_categories", ct);

    /// <summary>Fetches series listings, optionally filtered by category.</summary>
    public Task<List<SeriesStream>?> GetSeriesAsync(
        ProviderConfig provider, string? categoryId = null, CancellationToken ct = default)
    {
        var suffix = categoryId is null
            ? "&action=get_series"
            : $"&action=get_series&category_id={Uri.EscapeDataString(categoryId)}";
        return GetJsonAsync<List<SeriesStream>>(provider, suffix, ct);
    }

    /// <summary>Fetches season + episode detail for a single series.</summary>
    public Task<SeriesInfo?> GetSeriesInfoAsync(ProviderConfig provider, int seriesId, CancellationToken ct = default)
        => GetJsonAsync<SeriesInfo>(provider, $"&action=get_series_info&series_id={seriesId}", ct);

    // ── Stream URL builders ───────────────────────────────────────────────────

    /// <summary>Builds a live stream playback URL.</summary>
    public string BuildLiveStreamUrl(ProviderConfig provider, int streamId, string extension = "ts")
    {
        var (user, pass) = Credentials(provider);
        return $"{provider.BaseUrl}/live/{user}/{pass}/{streamId}.{extension}";
    }

    /// <summary>Builds a VOD stream playback URL.</summary>
    public string BuildVodStreamUrl(ProviderConfig provider, int streamId, string extension = "mp4")
    {
        var (user, pass) = Credentials(provider);
        return $"{provider.BaseUrl}/movie/{user}/{pass}/{streamId}.{extension}";
    }

    /// <summary>Builds a series episode playback URL.</summary>
    public string BuildSeriesStreamUrl(ProviderConfig provider, int episodeId, string extension = "mp4")
    {
        var (user, pass) = Credentials(provider);
        return $"{provider.BaseUrl}/series/{user}/{pass}/{episodeId}.{extension}";
    }

    /// <summary>
    /// Builds a catch-up (timeshift) URL.
    /// Format: {base}/timeshift/{user}/{pass}/{duration}/{Y-MM-DD:HH-mm}/{streamId}.ts
    /// </summary>
    public string BuildTimeshiftUrl(ProviderConfig provider, int streamId, int archiveDurationHours, DateTime startUtc)
    {
        var (user, pass) = Credentials(provider);
        var timestamp = startUtc.ToString("yyyy-MM-dd:HH-mm");
        return $"{provider.BaseUrl}/timeshift/{user}/{pass}/{archiveDurationHours}/{timestamp}/{streamId}.ts";
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task<T?> GetJsonAsync<T>(ProviderConfig provider, string actionSuffix, CancellationToken ct)
    {
        var url = BuildPlayerApiUrl(provider, actionSuffix);
        _logger.LogDebug("GET {SafeUrl}", RedactUrl(url));

        var http = CreateClient(provider);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var result = await http.GetFromJsonAsync<T>(url, JsonOptions, ct).ConfigureAwait(false);
                return result;
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
            {
                var delay = (int)(InitialBackoffMs * Math.Pow(BackoffFactor, attempt - 1));
                _logger.LogWarning(ex,
                    "Request to {SafeUrl} failed (attempt {Attempt}/{Max}); retrying in {Delay}ms",
                    RedactUrl(url), attempt, MaxAttempts, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Request to {SafeUrl} failed after {Max} attempts",
                    RedactUrl(url), MaxAttempts);
                return default;
            }
        }

        return default;
    }

    private HttpClient CreateClient(ProviderConfig provider)
    {
        var http = _httpClientFactory.CreateClient("Flux");
        http.DefaultRequestHeaders.UserAgent.TryParseAdd(provider.UserAgent);
        return http;
    }

    private static string BuildPlayerApiUrl(ProviderConfig provider, string suffix)
    {
        var (user, pass) = Credentials(provider);
        return $"{provider.BaseUrl}/player_api.php?username={user}&password={pass}{suffix}";
    }

    private static string BuildXmltvUrl(ProviderConfig provider)
    {
        var (user, pass) = Credentials(provider);
        return $"{provider.BaseUrl}/xmltv.php?username={user}&password={pass}";
    }

    private static (string user, string pass) Credentials(ProviderConfig provider)
    {
        var user = Uri.EscapeDataString(provider.Username);
        var pass = Uri.EscapeDataString(DecryptPassword(provider.EncryptedPassword));
        return (user, pass);
    }

    /// <summary>Replaces the credential segment in a URL with *** for safe logging.</summary>
    internal static string RedactUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var query = uri.Query;
            if (string.IsNullOrEmpty(query))
            {
                return url;
            }

            // Remove username= and password= values
            var sb = new StringBuilder(uri.GetLeftPart(UriPartial.Path));
            sb.Append("?username=***&password=***");
            foreach (var part in query.TrimStart('?').Split('&'))
            {
                if (!part.StartsWith("username=", StringComparison.OrdinalIgnoreCase) &&
                    !part.StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append('&');
                    sb.Append(part);
                }
            }

            return sb.ToString();
        }
        catch
        {
            return "***";
        }
    }

    private static string DecryptPassword(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encrypted));
        }
        catch
        {
            // Stored as plaintext during setup before encryption migration
            return encrypted;
        }
    }

    /// <summary>Encrypts a plaintext password for storage.</summary>
    public static string EncryptPassword(string plaintext)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
}
