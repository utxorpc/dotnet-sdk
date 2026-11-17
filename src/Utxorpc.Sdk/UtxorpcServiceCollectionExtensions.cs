using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Utxorpc.Sdk;

/// <summary>Registers shared clients with a service collection.</summary>
public static class UtxorpcServiceCollectionExtensions
{
    /// <summary>Registers a client created on first use and disposed with the service provider.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client when it is first resolved.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    [OverloadResolutionPriority(1)]
    public static IServiceCollection AddUtxorpcClient(this IServiceCollection services, Action<UtxorpcClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddUtxorpcClient((_, options) => configure(options));
    }

    /// <summary>Registers a client created on first use, configured with services from the provider, and disposed with it.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client when it is first resolved.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IServiceCollection AddUtxorpcClient(this IServiceCollection services, Action<IServiceProvider, UtxorpcClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddSingleton(provider => CreateClient(provider, configure));
        return services;
    }

    /// <summary>Registers a named client created on first use and disposed with the service provider.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client registration name.</param>
    /// <param name="configure">Configures the client when it is first resolved.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    [OverloadResolutionPriority(1)]
    public static IServiceCollection AddUtxorpcClient(this IServiceCollection services, string name, Action<UtxorpcClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddUtxorpcClient(name, (_, options) => configure(options));
    }

    /// <summary>Registers a named client created on first use, configured with services from the provider, and disposed with it.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client registration name.</param>
    /// <param name="configure">Configures the client when it is first resolved.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IServiceCollection AddUtxorpcClient(this IServiceCollection services, string name, Action<IServiceProvider, UtxorpcClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddKeyedSingleton(name, (provider, _) => CreateClient(provider, configure));
        return services;
    }

    private static UtxorpcClient CreateClient(IServiceProvider provider, Action<IServiceProvider, UtxorpcClientOptions> configure)
    {
        UtxorpcClientOptions options = new();
        configure(provider, options);
        return new UtxorpcClient(options);
    }
}
