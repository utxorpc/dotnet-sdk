using Grpc.Core;
using Utxorpc.Sdk.Internal;

namespace Utxorpc.Sdk;

/// <summary>Reads server streams while owning the lifetime of their calls.</summary>
public static class ServerStreamingCallExtensions
{
    /// <summary>Reads each response and disposes the call when enumeration ends.</summary>
    public static IAsyncEnumerable<T> ReadAllAsync<T>(this AsyncServerStreamingCall<T> call, CancellationToken cancellationToken = default) =>
        ServerStream.ReadAllAsync<AsyncServerStreamingCall<T>, T, T, StartedCall<T>>(call, cancellationToken);
}
