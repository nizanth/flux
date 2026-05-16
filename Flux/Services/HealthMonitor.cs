using System.Collections.Concurrent;
using Jellyfin.Plugin.Flux.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Services;

/// <summary>Tracks per-provider health status and surfaces last-error context.</summary>
public sealed class HealthMonitor
{
    private readonly ConcurrentDictionary<Guid, ProviderStatus> _statuses = new();
    private readonly ILogger<HealthMonitor> _logger;

    /// <summary>Initializes a new instance of <see cref="HealthMonitor"/>.</summary>
    public HealthMonitor(ILogger<HealthMonitor> logger)
    {
        _logger = logger;
    }

    /// <summary>Returns the current health status for a provider.</summary>
    public ProviderStatus GetStatus(Guid providerId)
        => _statuses.GetOrAdd(providerId, _ => new ProviderStatus(providerId));

    /// <summary>Marks a provider as healthy and clears any previous error.</summary>
    public void RecordSuccess(Guid providerId, string operationName)
    {
        var status = _statuses.GetOrAdd(providerId, _ => new ProviderStatus(providerId));
        lock (status)
        {
            status.Health = ProviderHealth.Ok;
            status.LastError = null;
            status.LastSuccessAt = DateTime.UtcNow;
        }

        _logger.LogDebug("Provider {ProviderId} health reset to OK after successful {Op}", providerId, operationName);
    }

    /// <summary>Records a failure for a provider, updating health accordingly.</summary>
    public void RecordFailure(Guid providerId, string operationName, Exception? ex, string? message = null)
    {
        var status = _statuses.GetOrAdd(providerId, _ => new ProviderStatus(providerId));
        lock (status)
        {
            status.Health = ProviderHealth.Failed;
            status.LastError = message ?? ex?.Message ?? "Unknown error";
            status.LastFailureAt = DateTime.UtcNow;
        }

        _logger.LogWarning(ex, "Provider {ProviderId} marked Failed after {Op}: {Message}",
            providerId, operationName, status.LastError);
    }

    /// <summary>Records a degraded state (partial failure).</summary>
    public void RecordDegraded(Guid providerId, string reason)
    {
        var status = _statuses.GetOrAdd(providerId, _ => new ProviderStatus(providerId));
        lock (status)
        {
            if (status.Health != ProviderHealth.Failed)
            {
                status.Health = ProviderHealth.Degraded;
            }

            status.LastError = reason;
        }

        _logger.LogInformation("Provider {ProviderId} marked Degraded: {Reason}", providerId, reason);
    }

    /// <summary>Returns all tracked provider statuses.</summary>
    public IReadOnlyCollection<ProviderStatus> GetAllStatuses()
        => _statuses.Values.ToList();
}

/// <summary>Mutable health record for a single provider.</summary>
public sealed class ProviderStatus
{
    /// <summary>Initializes a new instance of <see cref="ProviderStatus"/>.</summary>
    public ProviderStatus(Guid providerId)
    {
        ProviderId = providerId;
    }

    /// <summary>Gets the provider ID.</summary>
    public Guid ProviderId { get; }

    /// <summary>Gets or sets the current health level.</summary>
    public ProviderHealth Health { get; set; } = ProviderHealth.Unknown;

    /// <summary>Gets or sets the last error message, or null if healthy.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets the time of the last successful sync.</summary>
    public DateTime? LastSuccessAt { get; set; }

    /// <summary>Gets or sets the time of the most recent failure.</summary>
    public DateTime? LastFailureAt { get; set; }
}
