using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Utxorpc.Sdk;

/// <summary>Registers shared clients with a service collection.</summary>
public static class UtxorpcServiceCollectionExtensions
{
    private static int registrations;

    /// <summary>Registers a client created on first use and disposed with the service provider.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client at host start, or on first resolve without a host.</param>
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
    /// <param name="configure">Configures the client at host start, or on first resolve without a host.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IServiceCollection AddUtxorpcClient(this IServiceCollection services, Action<IServiceProvider, UtxorpcClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(services, null, configure);
    }

    /// <summary>Registers a named client created on first use and disposed with the service provider.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client registration name.</param>
    /// <param name="configure">Configures the client at host start, or on first resolve without a host.</param>
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
    /// <param name="configure">Configures the client at host start, or on first resolve without a host.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IServiceCollection AddUtxorpcClient(this IServiceCollection services, string name, Action<IServiceProvider, UtxorpcClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(services, name, configure);
    }

    private static IServiceCollection Add(IServiceCollection services, object? key, Action<IServiceProvider, UtxorpcClientOptions> configure)
    {
        string optionsName = $"Utxorpc.Sdk/{Interlocked.Increment(ref registrations)}";
        services.AddOptions<UtxorpcClientOptions>(optionsName)
            .Configure<IServiceProvider>((options, provider) => configure(provider, options)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<UtxorpcClientOptions>>(new OptionsCheck(optionsName, key as string));
        Func<IServiceProvider, UtxorpcClient> create = provider => new UtxorpcClient(
            provider.GetRequiredService<IOptionsMonitor<UtxorpcClientOptions>>().Get(optionsName));
        if (key is null) services.AddSingleton(create);
        else services.AddKeyedSingleton<UtxorpcClient>(key, (provider, _) => create(provider));
        return services;
    }

    private sealed class OptionsCheck(string optionsName, string? client) : IValidateOptions<UtxorpcClientOptions>
    {
        public ValidateOptionsResult Validate(string? name, UtxorpcClientOptions options)
        {
            if (name != optionsName) return ValidateOptionsResult.Skip;
            try
            {
                UtxorpcClient.ValidateOptions(options);
                return ValidateOptionsResult.Success;
            }
            catch (Exception exception) when (exception is ArgumentException or UriFormatException)
            {
                return ValidateOptionsResult.Fail($"Utxorpc client{(client is null ? "" : $" '{client}'")}: {exception.Message}");
            }
        }
    }
}
