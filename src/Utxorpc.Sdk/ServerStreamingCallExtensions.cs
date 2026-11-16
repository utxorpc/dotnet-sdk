using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Utxorpc.Sdk;

/// <summary>Reads server streams while owning the lifetime of their calls.</summary>
public static class ServerStreamingCallExtensions
{
    /// <summary>Reads each response and disposes the call when enumeration ends.</summary>
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(this AsyncServerStreamingCall<T> call, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using (call)
        {
            while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                yield return call.ResponseStream.Current;
            }
        }
    }
}
