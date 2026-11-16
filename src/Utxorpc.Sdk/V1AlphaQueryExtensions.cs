using Google.Protobuf;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Alpha.Query;

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1alpha query client.</summary>
public static class V1AlphaQueryExtensions
{
    /// <summary>Reads the current chain parameters.</summary>
    public static Task<ReadParamsResponse> ReadParamsAsync(this QueryService.QueryServiceClient client, CancellationToken cancellationToken = default) =>
        client.ReadParamsAsync(new ReadParamsRequest(), cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Reads outputs without copying hash memory; it must not change until the call returns.</summary>
    public static Task<ReadUtxosResponse> ReadUtxosAsync(this QueryService.QueryServiceClient client, ReadOnlySpan<OutputReference> references, CancellationToken cancellationToken = default)
    {
        ReadUtxosRequest request = new();
        request.Keys.Capacity = references.Length;
        for (int i = 0; i < references.Length; i++)
        {
            request.Keys.Add(new TxoRef { Hash = UnsafeByteOperations.UnsafeWrap(references[i].TxHash), Index = references[i].Index });
        }
        return client.ReadUtxosAsync(request, cancellationToken: cancellationToken).ResponseAsync;
    }

    /// <summary>Reads every matching output across all result pages.</summary>
    public static IAsyncEnumerable<AnyUtxoData> SearchAllUtxosAsync(this QueryService.QueryServiceClient client, UtxoPredicate predicate, int pageSize = 0, CancellationToken cancellationToken = default) =>
        Paging.ReadAllPagesAsync<QueryService.QueryServiceClient, (UtxoPredicate Predicate, int PageSize), SearchUtxosResponse, AnyUtxoData>(
            client, (predicate, pageSize),
            static (queryClient, state, startToken, ct) =>
            {
                SearchUtxosRequest request = new() { Predicate = state.Predicate };
                if (state.PageSize > 0) request.MaxItems = state.PageSize;
                if (startToken is not null) request.StartToken = startToken;
                return queryClient.SearchUtxosAsync(request, cancellationToken: ct).ResponseAsync;
            },
            static response => response.Items,
            static response => response.NextToken,
            cancellationToken);

    /// <summary>Reads outputs at an exact address; its memory must not change until enumeration ends.</summary>
    public static IAsyncEnumerable<AnyUtxoData> SearchUtxosByAddressAsync(this QueryService.QueryServiceClient client, ReadOnlyMemory<byte> address, int pageSize = 0, CancellationToken cancellationToken = default) =>
        client.SearchAllUtxosAsync(new UtxoPredicate
        {
            Match = new() { Cardano = new() { Address = new() { ExactAddress = UnsafeByteOperations.UnsafeWrap(address) } } }
        }, pageSize, cancellationToken);

    /// <summary>Reads a transaction without copying hash memory; it must not change until the call returns.</summary>
    public static Task<ReadTxResponse> ReadTxAsync(this QueryService.QueryServiceClient client, ReadOnlyMemory<byte> txHash, CancellationToken cancellationToken = default) =>
        client.ReadTxAsync(new ReadTxRequest { Hash = UnsafeByteOperations.UnsafeWrap(txHash) }, cancellationToken: cancellationToken).ResponseAsync;
}
