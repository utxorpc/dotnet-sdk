using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Utxorpc.Sdk.Internal;

namespace Utxorpc.Sdk;

/// <summary>Offers both versions of the generated clients on one channel.</summary>
/// <remarks>
/// For SDK-created channels, the default policy may retry calls that fail with Unavailable, even if
/// the server already ran them. It never retries SubmitTx or a stream after its first message.
/// Check with WaitForTxAsync or ReadTxAsync before resubmitting a transaction.
/// A caller-supplied channel or a configuration hook that replaces ServiceConfig uses the caller's
/// retry behavior; the caller is then responsible for SubmitTx retries.
/// By default, an SDK-created connection with an open call is pinged after receiving nothing for
/// KeepAliveInterval. Disposal closes only the channel and handler created here; transports
/// supplied by ConfigureChannel remain the caller's.
/// </remarks>
public sealed class UtxorpcClient : IDisposable
{
    private static readonly UtxorpcClientOptions Defaults = new();
    private GrpcChannel? ownedChannel;
    private SocketsHttpHandler? ownedHandler;

    /// <summary>Creates and owns a channel configured with the given options.</summary>
    public UtxorpcClient(UtxorpcClientOptions options)
        : this(ValidateOptions(options), options.Headers, options.MaxAttempts, options.KeepAliveInterval, options.ConfigureChannel)
    {
    }

    /// <summary>Creates and owns a channel to the given URL.</summary>
    public UtxorpcClient(string url, IEnumerable<KeyValuePair<string, string>>? headers = null, Action<GrpcChannelOptions>? configureChannel = null)
        : this(url, headers, Defaults.MaxAttempts, Defaults.KeepAliveInterval, configureChannel)
    {
    }

    private UtxorpcClient(string url, IEnumerable<KeyValuePair<string, string>>? headers, int maxAttempts,
        TimeSpan keepAliveInterval, Action<GrpcChannelOptions>? configureChannel)
    {
        ArgumentNullException.ThrowIfNull(url);
        Metadata metadata = CreateHeaders(headers);
        GrpcChannelOptions options = new() { ThrowOperationCanceledOnCancellation = true };
        if (maxAttempts > 1) options.ServiceConfig = CreateServiceConfig(maxAttempts);
        ownedHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = keepAliveInterval,
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };
        options.HttpHandler = ownedHandler;
        configureChannel?.Invoke(options);
        if (options.HttpClient is not null && ReferenceEquals(options.HttpHandler, ownedHandler)) options.HttpHandler = null;
        ownedChannel = GrpcChannel.ForAddress(url, options);
        (V1Alpha, V1Beta) = CreateClients(ownedChannel, metadata);
    }

    /// <summary>Uses a caller-owned channel without changing its settings or lifetime.</summary>
    public UtxorpcClient(ChannelBase channel, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        (V1Alpha, V1Beta) = CreateClients(channel, CreateHeaders(headers));
    }

    /// <summary>Gets the v1alpha generated service clients.</summary>
    public V1AlphaClients V1Alpha { get; }

    /// <summary>Gets the v1beta generated service clients.</summary>
    public V1BetaClients V1Beta { get; }

    /// <summary>Closes only the channel and handler this client created.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref ownedChannel, null)?.Dispose();
        Interlocked.Exchange(ref ownedHandler, null)?.Dispose();
    }

    private static string ValidateOptions(UtxorpcClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Url);
        if (options.MaxAttempts is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(options.MaxAttempts));
        if (options.KeepAliveInterval != Timeout.InfiniteTimeSpan && options.KeepAliveInterval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(options.KeepAliveInterval));
        return options.Url;
    }

    private static ServiceConfig CreateServiceConfig(int maxAttempts) => new()
    {
        MethodConfigs =
        {
            new MethodConfig
            {
                Names = { MethodName.Default },
                RetryPolicy = new RetryPolicy
                {
                    MaxAttempts = maxAttempts,
                    InitialBackoff = TimeSpan.FromSeconds(1),
                    MaxBackoff = TimeSpan.FromSeconds(5),
                    BackoffMultiplier = 1.5,
                    RetryableStatusCodes = { StatusCode.Unavailable }
                }
            },
            new MethodConfig
            {
                Names =
                {
                    new MethodName { Service = Utxorpc.V1Alpha.Submit.SubmitService.Descriptor.FullName, Method = nameof(Utxorpc.V1Alpha.Submit.SubmitService.SubmitServiceBase.SubmitTx) },
                    new MethodName { Service = Utxorpc.V1Beta.Submit.SubmitService.Descriptor.FullName, Method = nameof(Utxorpc.V1Beta.Submit.SubmitService.SubmitServiceBase.SubmitTx) }
                }
            }
        }
    };

    private static Metadata CreateHeaders(IEnumerable<KeyValuePair<string, string>>? headers)
    {
        Metadata metadata = new();
        if (headers is not null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                metadata.Add(header.Key, header.Value);
            }
        }
        return metadata;
    }

    private static (V1AlphaClients, V1BetaClients) CreateClients(ChannelBase channel, Metadata headers)
    {
        CallInvoker invoker = channel.CreateCallInvoker();
        if (headers.Count > 0)
        {
            invoker = new HeaderCallInvoker(invoker, headers);
        }
        return (new V1AlphaClients(invoker), new V1BetaClients(invoker));
    }
}
