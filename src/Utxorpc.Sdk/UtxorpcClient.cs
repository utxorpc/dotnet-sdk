using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.Sdk.Internal;

namespace Utxorpc.Sdk;

/// <summary>Offers both versions of the generated clients on one channel.</summary>
public sealed class UtxorpcClient : IDisposable
{
    private GrpcChannel? ownedChannel;

    /// <summary>Creates and owns a channel to the given URL.</summary>
    public UtxorpcClient(string url, IEnumerable<KeyValuePair<string, string>>? headers = null, Action<GrpcChannelOptions>? configureChannel = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        Metadata metadata = CreateHeaders(headers);
        GrpcChannelOptions options = new() { ThrowOperationCanceledOnCancellation = true };
        configureChannel?.Invoke(options);
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

    /// <summary>Closes the channel only when this client created it.</summary>
    public void Dispose() => Interlocked.Exchange(ref ownedChannel, null)?.Dispose();

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
