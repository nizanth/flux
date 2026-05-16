using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Jellyfin.Plugin.Flux.Api.Dto;

namespace Jellyfin.Plugin.Flux.Services;

/// <summary>
/// In-memory cache for Xtream Codes catalog data (live streams, VOD, series, categories).
/// Keyed by provider ID to support multiple providers simultaneously.
/// </summary>
public sealed class CatalogCache
{
    private readonly ConcurrentDictionary<Guid, ProviderCatalog> _catalogs = new();

    /// <summary>
    /// Gets or creates the catalog for the specified provider.
    /// </summary>
    public ProviderCatalog GetOrCreate(Guid providerId)
        => _catalogs.GetOrAdd(providerId, _ => new ProviderCatalog());

    /// <summary>
    /// Removes all cached data for the specified provider.
    /// </summary>
    public void Invalidate(Guid providerId)
        => _catalogs.TryRemove(providerId, out _);

    /// <summary>
    /// Removes all cached data for all providers.
    /// </summary>
    public void InvalidateAll()
        => _catalogs.Clear();
}

/// <summary>
/// Holds cached catalog data for a single provider.
/// </summary>
public sealed class ProviderCatalog
{
    /// <summary>Gets or sets the cached list of live stream categories.</summary>
    public List<Category>? LiveCategories { get; set; }

    /// <summary>Gets or sets the cached list of live streams.</summary>
    public List<LiveStream>? LiveStreams { get; set; }

    /// <summary>Gets or sets the time the live stream data was last refreshed.</summary>
    public DateTime? LiveStreamsRefreshedAt { get; set; }

    /// <summary>Gets or sets the cached list of VOD categories.</summary>
    public List<Category>? VodCategories { get; set; }

    /// <summary>Gets or sets the cached list of VOD streams.</summary>
    public List<VodStream>? VodStreams { get; set; }

    /// <summary>Gets or sets the time the VOD data was last refreshed.</summary>
    public DateTime? VodRefreshedAt { get; set; }

    /// <summary>Gets or sets the cached list of series categories.</summary>
    public List<Category>? SeriesCategories { get; set; }

    /// <summary>Gets or sets the cached list of series.</summary>
    public List<SeriesStream>? Series { get; set; }

    /// <summary>Gets or sets the time the series data was last refreshed.</summary>
    public DateTime? SeriesRefreshedAt { get; set; }
}
