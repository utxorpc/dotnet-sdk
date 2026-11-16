using Google.Protobuf;
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
}
