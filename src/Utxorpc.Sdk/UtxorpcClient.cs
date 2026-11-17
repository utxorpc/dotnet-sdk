using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Utxorpc.Sdk.Internal;

namespace Utxorpc.Sdk;

/// <summary>Offers both versions of the generated clients on one channel.</summary>
/// <remarks>
/// For SDK-created channels, the default policy may retry unary calls that fail with Unavailable,
/// even if the server already ran them. It never retries SubmitTx or a server stream.
/// Stream failures are reported to the caller, who handles reconnecting and, where supported,
/// resuming from a saved point. FollowTip and WatchTx support intersection points.
/// Check with WaitForTxAsync or ReadTxAsync before resubmitting a transaction.
/// A caller-supplied channel or a configuration hook that replaces ServiceConfig uses the caller's
/// retry behavior; the caller is then responsible for SubmitTx retries.
/// By default, an SDK-created connection with an open call is pinged after receiving nothing for
/// KeepAliveInterval. Disposal closes only the channel and handler created here; transports
/// supplied by ConfigureChannel remain the caller's.
/// </remarks>
public sealed class UtxorpcClient : IDisposable
{
    private const string UrlRequired = "UtxorpcClientOptions.Url is required.";
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);
    private static readonly UtxorpcClientOptions Defaults = new();
    private GrpcChannel? ownedChannel;
    private SocketsHttpHandler? ownedHandler;

    /// <summary>Creates and owns a channel configured with the given options.</summary>
    /// <param name="options">The client options, including the required URL.</param>
    /// <exception cref="ArgumentNullException">The options or their URL is null.</exception>
    /// <exception cref="ArgumentException">The URL is blank or a header name is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A retry, keep-alive or timeout option is out of range.</exception>
    /// <exception cref="UriFormatException">The URL is not a valid URI.</exception>
    public UtxorpcClient(UtxorpcClientOptions options)
        : this(ValidateOptions(options), options.Headers, options.MaxAttempts, options.KeepAliveInterval, options.DefaultTimeout, options.ConfigureChannel)
    {
    }

    /// <summary>Creates and owns a channel to the given URL.</summary>
    /// <param name="url">The server URL, including its scheme.</param>
    /// <param name="headers">The headers sent with every call, or null for none.</param>
    /// <param name="configureChannel">An optional callback that changes the channel defaults.</param>
    /// <exception cref="ArgumentNullException">The URL is null.</exception>
    /// <exception cref="ArgumentException">The URL is blank or a header name is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A channel option is out of range.</exception>
    /// <exception cref="UriFormatException">The URL is not a valid URI.</exception>
    public UtxorpcClient(string url, IEnumerable<KeyValuePair<string, string>>? headers = null, Action<GrpcChannelOptions>? configureChannel = null)
        : this(url, headers, Defaults.MaxAttempts, Defaults.KeepAliveInterval, Defaults.DefaultTimeout, configureChannel)
    {
    }

    private UtxorpcClient(string url, IEnumerable<KeyValuePair<string, string>>? headers, int maxAttempts,
        TimeSpan keepAliveInterval, TimeSpan defaultTimeout, Action<GrpcChannelOptions>? configureChannel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
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
        try
        {
            ownedChannel = GrpcChannel.ForAddress(url, options);
        }
        catch (UriFormatException exception)
        {
            throw new UriFormatException("The URL is not a valid URI; it needs a scheme, such as http:// or https://.", exception);
        }
        (V1Alpha, V1Beta) = CreateClients(ownedChannel, metadata, defaultTimeout);
    }

    /// <summary>Uses a caller-owned channel without changing its settings or lifetime.</summary>
    /// <param name="channel">The caller-owned channel.</param>
    /// <param name="headers">The headers sent with every call, or null for none.</param>
    /// <exception cref="ArgumentNullException">The channel is null.</exception>
    /// <exception cref="ArgumentException">A header name is invalid.</exception>
    public UtxorpcClient(ChannelBase channel, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        (V1Alpha, V1Beta) = CreateClients(channel, CreateHeaders(headers), Defaults.DefaultTimeout);
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
        if (options.MaxAttempts is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxAttempts, "MaxAttempts must be from 1 to 5.");
        if (options.KeepAliveInterval != Timeout.InfiniteTimeSpan && options.KeepAliveInterval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(options), options.KeepAliveInterval, "KeepAliveInterval must be at least 1 second, or infinite.");
        if (options.DefaultTimeout != Timeout.InfiniteTimeSpan && (options.DefaultTimeout <= TimeSpan.Zero || options.DefaultTimeout > MaxTimeout))
            throw new ArgumentOutOfRangeException(nameof(options), options.DefaultTimeout, "DefaultTimeout must be positive and at most 2147483647 milliseconds (about 24.8 days), or infinite.");
        if (string.IsNullOrWhiteSpace(options.Url))
            throw options.Url is null ? new ArgumentNullException(nameof(options), UrlRequired) : new ArgumentException(UrlRequired, nameof(options));
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
            // Exclude server streams: retry can turn cancellation before the first message into RpcException(Cancelled); see https://github.com/grpc/grpc-dotnet/issues/2788.
            new MethodConfig
            {
                Names =
                {
                    new MethodName { Service = Utxorpc.V1Alpha.Submit.SubmitService.Descriptor.FullName, Method = nameof(Utxorpc.V1Alpha.Submit.SubmitService.SubmitServiceBase.SubmitTx) },
                    new MethodName { Service = Utxorpc.V1Beta.Submit.SubmitService.Descriptor.FullName, Method = nameof(Utxorpc.V1Beta.Submit.SubmitService.SubmitServiceBase.SubmitTx) },
                    new MethodName { Service = Utxorpc.V1Alpha.Submit.SubmitService.Descriptor.FullName, Method = nameof(Utxorpc.V1Alpha.Submit.SubmitService.SubmitServiceBase.WaitForTx) },
                    new MethodName { Service = Utxorpc.V1Alpha.Submit.SubmitService.Descriptor.FullName, Method = nameof(Utxorpc.V1Alpha.Submit.SubmitService.SubmitServiceBase.WatchMempool) },
                    new MethodName { Service = Utxorpc.V1Alpha.Sync.SyncService.Descriptor.FullName, Method = nameof(Utxorpc.V1Alpha.Sync.SyncService.SyncServiceBase.FollowTip) },
                    new MethodName { Service = Utxorpc.V1Alpha.Watch.WatchService.Descriptor.FullName, Method = nameof(Utxorpc.V1Alpha.Watch.WatchService.WatchServiceBase.WatchTx) },
                    new MethodName { Service = Utxorpc.V1Beta.Submit.SubmitService.Descriptor.FullName, Method = nameof(Utxorpc.V1Beta.Submit.SubmitService.SubmitServiceBase.WaitForTx) },
                    new MethodName { Service = Utxorpc.V1Beta.Submit.SubmitService.Descriptor.FullName, Method = nameof(Utxorpc.V1Beta.Submit.SubmitService.SubmitServiceBase.WatchMempool) },
                    new MethodName { Service = Utxorpc.V1Beta.Sync.SyncService.Descriptor.FullName, Method = nameof(Utxorpc.V1Beta.Sync.SyncService.SyncServiceBase.FollowTip) },
                    new MethodName { Service = Utxorpc.V1Beta.Watch.WatchService.Descriptor.FullName, Method = nameof(Utxorpc.V1Beta.Watch.WatchService.WatchServiceBase.WatchTx) }
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

    private static (V1AlphaClients, V1BetaClients) CreateClients(ChannelBase channel, Metadata headers, TimeSpan defaultTimeout)
    {
        CallInvoker invoker = new ClientCallInvoker(channel.CreateCallInvoker(), headers, defaultTimeout);
        return (new V1AlphaClients(invoker), new V1BetaClients(invoker));
    }
}
