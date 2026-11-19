using Grpc.Core;
using Utxorpc.Sdk.Internal;

namespace Utxorpc.Sdk;

/// <summary>Reads server streams while owning the lifetime of their calls.</summary>
public static class ServerStreamingCallExtensions
{
    /// <summary>Reads each response and disposes the call when enumeration ends.</summary>
    /// <param name="call">The call whose lifetime the sequence owns.</param>
    /// <param name="cancellationToken">Cancels enumeration.</param>
    /// <returns>The responses, in server order.</returns>
    /// <exception cref="Grpc.Core.RpcException">The call failed or its deadline expired while enumerating.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled on a channel configured to report cancellation.</exception>
    public static IAsyncEnumerable<T> ReadAllAsync<T>(this AsyncServerStreamingCall<T> call, CancellationToken cancellationToken = default) =>
        ServerStream.ReadAllAsync<AsyncServerStreamingCall<T>, T, T, StartedCall<T>>(call, cancellationToken);
}
