using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.Channels;
using Jellyfin.Plugin.Flux.LiveTv;
using Jellyfin.Plugin.Flux.ScheduledTasks;
using Jellyfin.Plugin.Flux.Services;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Flux;

/// <summary>Registers all Flux plugin services with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers all Flux plugin services.</summary>
    public static IServiceCollection AddFluxPlugin(this IServiceCollection services)
    {
        // HTTP
        services.AddHttpClient("Flux");

        // Core services
        services.AddSingleton<XtreamApiClient>();
        services.AddSingleton<CatalogCache>();
        services.AddSingleton<XmltvParser>();
        services.AddSingleton<HealthMonitor>();
        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<CatalogSyncService>();

        // Jellyfin integrations
        services.AddSingleton<ILiveTvService, FluxLiveTvService>();
        services.AddSingleton<IChannel, VodChannel>();
        services.AddSingleton<VodMetadataService>();
        services.AddSingleton<IChannel, SeriesChannel>();
        services.AddSingleton<SeriesMetadataService>();

        // Scheduled tasks
        services.AddSingleton<IScheduledTask, SyncLiveTask>();
        services.AddSingleton<IScheduledTask, SyncVodTask>();
        services.AddSingleton<IScheduledTask, SyncSeriesTask>();

        // Startup background service
        services.AddHostedService<FluxStartup>();

        return services;
    }
}
