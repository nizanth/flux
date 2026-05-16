using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.LiveTv;
using Jellyfin.Plugin.Flux.Services;
using MediaBrowser.Controller.LiveTv;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Flux;

/// <summary>
/// Extension methods for registering Flux plugin services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Flux plugin services into the provided <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddFluxPlugin(this IServiceCollection services)
    {
        services.AddHttpClient<XtreamApiClient>();
        services.AddSingleton<CatalogCache>();
        services.AddSingleton<XtreamApiClient>();
        services.AddSingleton<XmltvParser>();
        services.AddSingleton<CatalogSyncService>();
        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<ILiveTvService, FluxLiveTvService>();
        return services;
    }
}
