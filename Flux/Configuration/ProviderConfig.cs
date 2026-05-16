using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Flux.Configuration;

/// <summary>
/// Represents the health status of an Xtream Codes provider.
/// </summary>
public enum ProviderHealth
{
    /// <summary>
    /// Health status is not yet known.
    /// </summary>
    Unknown,

    /// <summary>
    /// Provider is reachable and functioning normally.
    /// </summary>
    Ok,

    /// <summary>
    /// Provider is reachable but experiencing issues.
    /// </summary>
    Degraded,

    /// <summary>
    /// Provider is unreachable or returning errors.
    /// </summary>
    Failed
}

/// <summary>
/// Configuration for a single Xtream Codes IPTV provider.
/// </summary>
public class ProviderConfig
{
    /// <summary>
    /// Gets or sets the unique identifier for this provider.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the human-readable display name for this provider.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base URL of the Xtream Codes server (e.g. http://provider.example.com:8080).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Xtream Codes username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the encrypted Xtream Codes password.
    /// </summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user-agent string sent with HTTP requests to this provider.
    /// </summary>
    public string UserAgent { get; set; } = "Flux/1.0";

    /// <summary>
    /// Gets or sets a value indicating whether HTTPS is required for this provider.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of category IDs that are enabled for this provider.
    /// An empty list means all categories are enabled.
    /// </summary>
    public List<string> EnabledCategoryIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp of the last successful sync for this provider.
    /// </summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>
    /// Gets or sets the current health status of this provider.
    /// </summary>
    public ProviderHealth Health { get; set; } = ProviderHealth.Unknown;

    /// <summary>
    /// Gets or sets the last error message encountered when communicating with this provider.
    /// </summary>
    public string? LastError { get; set; }
}
