using Jellyfin.Plugin.Flux.Configuration;

namespace Jellyfin.Plugin.Flux.Services;

/// <summary>Provides access to the list of configured providers from the plugin configuration.</summary>
public sealed class ProviderRegistry
{
    /// <summary>Returns all configured providers.</summary>
    public IReadOnlyList<ProviderConfig> GetAll()
        => Plugin.Instance?.Configuration.Providers ?? [];

    /// <summary>Returns a single provider by ID, or null.</summary>
    public ProviderConfig? GetById(Guid id)
        => GetAll().FirstOrDefault(p => p.Id == id);

    /// <summary>Adds or replaces a provider and saves the configuration.</summary>
    public void Save(ProviderConfig provider)
    {
        var config = Plugin.Instance!.Configuration;
        var idx = config.Providers.FindIndex(p => p.Id == provider.Id);
        if (idx >= 0)
        {
            config.Providers[idx] = provider;
        }
        else
        {
            config.Providers.Add(provider);
        }

        Plugin.Instance.SaveConfiguration();
    }

    /// <summary>Removes a provider by ID and saves the configuration.</summary>
    public bool Remove(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        var removed = config.Providers.RemoveAll(p => p.Id == id) > 0;
        if (removed)
        {
            Plugin.Instance.SaveConfiguration();
        }

        return removed;
    }
}
