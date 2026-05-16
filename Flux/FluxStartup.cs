using Jellyfin.Plugin.Flux.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux;

/// <summary>
/// Background startup service: kicks off an initial catalog sync shortly after Jellyfin boots.
/// Fire-and-forget so boot is not blocked (NFR-REL-004).
/// </summary>
public sealed class FluxStartup : BackgroundService
{
    private readonly CatalogSyncService _sync;
    private readonly ILogger<FluxStartup> _logger;

    /// <summary>Initializes a new instance of <see cref="FluxStartup"/>.</summary>
    public FluxStartup(CatalogSyncService sync, ILogger<FluxStartup> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief delay to let Jellyfin finish its own startup sequence
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("Flux: starting initial catalog sync");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromMinutes(15));
            await _sync.SyncAllAsync(cts.Token).ConfigureAwait(false);
            _logger.LogInformation("Flux: initial catalog sync complete");
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Flux: initial catalog sync timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flux: initial catalog sync failed");
        }
    }
}
