using Microsoft.Extensions.DependencyInjection;

namespace Utxorpc.Sdk;

/// <summary>Registers shared clients with a service collection.</summary>
public static class UtxorpcServiceCollectionExtensions
{
    /// <summary>Registers a client created on first use and disposed with the service provider.</summary>
    public static IServiceCollection AddUtxorpcClient(this IServiceCollection services, Action<UtxorpcClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddSingleton(_ => CreateClient(configure));
        return services;
    }

    /// <summary>Registers a named client created on first use and disposed with the service provider.</summary>
    public static IServiceCollection AddUtxorpcClient(this IServiceCollection services, string name, Action<UtxorpcClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddKeyedSingleton(name, (_, _) => CreateClient(configure));
        return services;
    }

    private static UtxorpcClient CreateClient(Action<UtxorpcClientOptions> configure)
    {
        UtxorpcClientOptions options = new();
        configure(options);
        return new UtxorpcClient(options);
    }
}
