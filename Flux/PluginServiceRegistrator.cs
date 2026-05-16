using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Flux;

/// <summary>
/// Registers Flux services with Jellyfin's DI container.
/// Jellyfin discovers this class by scanning the assembly for IPluginServiceRegistrator.
/// Must have a parameterless constructor.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddFluxPlugin();
    }
}
