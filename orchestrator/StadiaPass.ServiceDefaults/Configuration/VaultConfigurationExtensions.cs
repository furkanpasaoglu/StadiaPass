using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace StadiaPass.ServiceDefaults.Configuration;

internal sealed class VaultConfigurationSource(VaultOptions options) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new VaultConfigurationProvider(options);
}

public static class VaultConfigurationExtensions
{
    /// <summary>
    /// Puts Vault in front of every other configuration source. Call it before anything reads configuration -
    /// a connection string is resolved while the container is being built, so a source added afterwards would
    /// arrive too late to matter.
    /// </summary>
    /// <remarks>
    /// When no address or token is present the call does nothing, so a project can still be run on its own
    /// against local settings. What is never allowed is a secret sitting in a tracked file as a fallback:
    /// the options that carry one are <c>[Required]</c> and validated at startup, so a missing secret stops
    /// the process instead of quietly running on a default someone committed months ago.
    /// </remarks>
    public static TBuilder AddVaultConfiguration<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var options = builder.Configuration.GetSection(VaultOptions.SectionName).Get<VaultOptions>()
                      ?? new VaultOptions();

        if (!options.IsConfigured)
        {
            return builder;
        }

        builder.Configuration.Add(new VaultConfigurationSource(options));

        return builder;
    }
}
