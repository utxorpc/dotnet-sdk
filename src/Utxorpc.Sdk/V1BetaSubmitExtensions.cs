using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Beta.Submit;

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1beta submission client.</summary>
public static class V1BetaSubmitExtensions
{
    /// <summary>Submits bytes without copying their memory; it must not change until the call returns.</summary>
    public static Task<SubmitTxResponse> SubmitTxAsync(this SubmitService.SubmitServiceClient client, ReadOnlyMemory<byte> tx, CancellationToken cancellationToken = default) =>
        client.SubmitTxAsync(new SubmitTxRequest { Tx = new() { Raw = UnsafeByteOperations.UnsafeWrap(tx) } }, cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Watches a transaction; hash memory must not change until enumeration ends.</summary>
    public static IAsyncEnumerable<WaitForTxResponse> WaitForTxAsync(this SubmitService.SubmitServiceClient client, ReadOnlyMemory<byte> txHash, CancellationToken cancellationToken = default)
    {
        WaitForTxRequest request = new();
        request.Ref.Capacity = 1;
        request.Ref.Add(UnsafeByteOperations.UnsafeWrap(txHash));
        return client.WaitForTx(request, cancellationToken: cancellationToken).ReadAllAsync(cancellationToken);
    }

    /// <summary>Evaluates transaction bytes without copying their memory; it must not change until the call completes.</summary>
    public static Task<EvalTxResponse> EvalTxAsync(this SubmitService.SubmitServiceClient client, ReadOnlyMemory<byte> tx, CancellationToken cancellationToken = default) =>
        client.EvalTxAsync(new EvalTxRequest { Tx = new() { Raw = UnsafeByteOperations.UnsafeWrap(tx) } }, cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Reads the transactions currently in the mempool.</summary>
    public static Task<ReadMempoolResponse> ReadMempoolAsync(this SubmitService.SubmitServiceClient client, CancellationToken cancellationToken = default) =>
        client.ReadMempoolAsync(new ReadMempoolRequest(), cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Watches mempool changes matching the predicate, or all changes when it is null.</summary>
    public static IAsyncEnumerable<WatchMempoolResponse> WatchMempoolAsync(this SubmitService.SubmitServiceClient client, TxPredicate? predicate, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.WatchMempool(new WatchMempoolRequest { Predicate = predicate, FieldMask = fieldMask }, cancellationToken: cancellationToken).ReadAllAsync(cancellationToken);

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
