using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.V1Alpha.Submit;

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1alpha submission client.</summary>
public static class V1AlphaSubmitExtensions
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
}
