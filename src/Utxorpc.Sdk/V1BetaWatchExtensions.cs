using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Internal;
using Utxorpc.Sdk.V1Beta;
using Utxorpc.V1Beta.Watch;
using WatchTxCall = (Utxorpc.V1Beta.Watch.WatchService.WatchServiceClient Client, Utxorpc.V1Beta.Watch.WatchTxRequest Request);

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1beta watch client.</summary>
public static class V1BetaWatchExtensions
{
    /// <summary>Watches transactions; an optional point's hash memory must not change until enumeration ends.</summary>
    /// <remarks>A null predicate watches all transactions; a null point sends no intersection. Each message is a <see cref="WatchTxEvent"/>; one without an action this SDK knows ends the stream with <see cref="InvalidOperationException"/>; the generated <c>WatchTx</c> call returns the Spec messages.</remarks>
    public static IAsyncEnumerable<WatchTxEvent> WatchTxAsync(this WatchService.WatchServiceClient client, TxPredicate? predicate, BlockPoint? from, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        WatchTxRequest request = new() { Predicate = predicate, FieldMask = fieldMask };
        if (from is BlockPoint point)
        {
            request.Intersect.Capacity = 1;
            request.Intersect.Add(new BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) });
        }
        return ServerStream.ReadAllAsync<WatchTxCall, WatchTxResponse, WatchTxEvent, WatchTxEvent>((client, request), cancellationToken);
    }

    extension(TxPredicate)
    {
        /// <summary>Matches any address a transaction touches in inputs, outputs or collateral; byte memory is wrapped, not copied, and must not change while the predicate is in use.</summary>
        public static TxPredicate HasAddress(ReadOnlyMemory<byte> exactAddress = default, ReadOnlyMemory<byte> paymentPart = default, ReadOnlyMemory<byte> delegationPart = default) =>
            new() { Match = new() { Cardano = new() { HasAddress = Predicates.V1BetaAddress(exactAddress, paymentPart, delegationPart) } } };

        /// <summary>Matches an asset a transaction moves; byte memory is wrapped, not copied, and must not change while the predicate is in use. An empty name means any name; for an asset whose name is empty, build the v1beta pattern with AssetName = ByteString.Empty (v1alpha cannot express it).</summary>
        public static TxPredicate MovesAsset(ReadOnlyMemory<byte> policyId = default, ReadOnlyMemory<byte> assetName = default) =>
            new() { Match = new() { Cardano = new() { MovesAsset = Predicates.V1BetaAsset(policyId, assetName) } } };

        /// <summary>Matches when all supplied predicates match.</summary>
        public static TxPredicate AllOf(params ReadOnlySpan<TxPredicate> predicates)
        {
            TxPredicate result = new();
            Predicates.Fill(result.AllOf, predicates, nameof(predicates));
            return result;
        }

        /// <summary>Matches when any supplied predicate matches.</summary>
        public static TxPredicate AnyOf(params ReadOnlySpan<TxPredicate> predicates)
        {
            TxPredicate result = new();
            Predicates.Fill(result.AnyOf, predicates, nameof(predicates));
            return result;
        }

        /// <summary>Matches when the supplied predicate does not match.</summary>
        public static TxPredicate Not(TxPredicate predicate)
        {
            TxPredicate result = new();
            Predicates.Fill(result.Not, [predicate], nameof(predicate));
            return result;
        }
    }
}
