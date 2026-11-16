using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.V1Beta.Watch;

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1beta watch client.</summary>
public static class V1BetaWatchExtensions
{
    /// <summary>Watches transactions; an optional point's hash memory must not change until enumeration ends.</summary>
    /// <remarks>A null predicate watches all transactions; a null point sends no intersection.</remarks>
    public static IAsyncEnumerable<WatchTxResponse> WatchTxAsync(this WatchService.WatchServiceClient client, TxPredicate? predicate, BlockPoint? from, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        WatchTxRequest request = new() { Predicate = predicate, FieldMask = fieldMask };
        if (from is BlockPoint point)
        {
            request.Intersect.Capacity = 1;
            request.Intersect.Add(new BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) });
        }
        return client.WatchTx(request, cancellationToken: cancellationToken).ReadAllAsync(cancellationToken);
    }
}
