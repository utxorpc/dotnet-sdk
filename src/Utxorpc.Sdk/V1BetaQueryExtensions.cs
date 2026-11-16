using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Beta.Query;

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1beta query client.</summary>
public static class V1BetaQueryExtensions
{
    /// <summary>Reads the current chain parameters.</summary>
    public static Task<ReadParamsResponse> ReadParamsAsync(this QueryService.QueryServiceClient client, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.ReadParamsAsync(new ReadParamsRequest { FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Reads outputs without copying hash memory; it must not change until the call returns.</summary>
    public static Task<ReadUtxosResponse> ReadUtxosAsync(this QueryService.QueryServiceClient client, ReadOnlySpan<OutputReference> references, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        ReadUtxosRequest request = new() { FieldMask = fieldMask };
        request.Keys.Capacity = references.Length;
        for (int i = 0; i < references.Length; i++)
        {
            request.Keys.Add(new TxoRef { Hash = UnsafeByteOperations.UnsafeWrap(references[i].TxHash), Index = references[i].Index });
        }
        return client.ReadUtxosAsync(request, cancellationToken: cancellationToken).ResponseAsync;
    }

    /// <summary>Reads every matching output across all result pages.</summary>
    public static IAsyncEnumerable<AnyUtxoData> SearchAllUtxosAsync(this QueryService.QueryServiceClient client, UtxoPredicate predicate, int pageSize = 0, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        Paging.ReadAllPagesAsync<QueryService.QueryServiceClient, (UtxoPredicate Predicate, int PageSize, FieldMask? Mask), string, SearchUtxosResponse, AnyUtxoData>(
            client, (predicate, pageSize, fieldMask),
            static (queryClient, state, startToken, ct) =>
            {
                SearchUtxosRequest request = new() { Predicate = state.Predicate, FieldMask = state.Mask };
                if (state.PageSize > 0) request.MaxItems = state.PageSize;
                if (startToken is not null) request.StartToken = startToken;
                return queryClient.SearchUtxosAsync(request, cancellationToken: ct).ResponseAsync;
            },
            static response => response.Items,
            static response => response.HasNextToken && response.NextToken.Length > 0 ? response.NextToken : null,
            cancellationToken);

    /// <summary>Reads outputs at an exact address; its memory must not change until enumeration ends.</summary>
    public static IAsyncEnumerable<AnyUtxoData> SearchUtxosByAddressAsync(this QueryService.QueryServiceClient client, ReadOnlyMemory<byte> address, int pageSize = 0, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.SearchAllUtxosAsync(UtxoPredicate.Address(exactAddress: address), pageSize, fieldMask, cancellationToken);

    /// <summary>Reads a transaction without copying hash memory; it must not change until the call returns.</summary>
    public static Task<ReadTxResponse> ReadTxAsync(this QueryService.QueryServiceClient client, ReadOnlyMemory<byte> txHash, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.ReadTxAsync(new ReadTxRequest { Hash = UnsafeByteOperations.UnsafeWrap(txHash), FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Reads the requested Cardano ledger state.</summary>
    public static Task<ReadStateResponse> ReadStateAsync(this QueryService.QueryServiceClient client, Utxorpc.V1Beta.Cardano.StateQuery query, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.ReadStateAsync(new ReadStateRequest { Query = new AnyChainStateQuery { Cardano = query }, FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Reads the chain's genesis configuration.</summary>
    public static Task<ReadGenesisResponse> ReadGenesisAsync(this QueryService.QueryServiceClient client, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.ReadGenesisAsync(new ReadGenesisRequest { FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Reads the chain's era summaries.</summary>
    public static Task<ReadEraSummaryResponse> ReadEraSummaryAsync(this QueryService.QueryServiceClient client, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.ReadEraSummaryAsync(new ReadEraSummaryRequest { FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Reads data without copying hash memory; it must not change until the call completes.</summary>
    public static Task<ReadDataResponse> ReadDataAsync(this QueryService.QueryServiceClient client, ReadOnlySpan<ReadOnlyMemory<byte>> datumHashes, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        ReadDataRequest request = new() { FieldMask = fieldMask };
        request.Keys.Capacity = datumHashes.Length;
        for (int i = 0; i < datumHashes.Length; i++)
        {
            request.Keys.Add(UnsafeByteOperations.UnsafeWrap(datumHashes[i]));
        }
        return client.ReadDataAsync(request, cancellationToken: cancellationToken).ResponseAsync;
    }

    extension(UtxoPredicate)
    {
        /// <summary>Matches an output's address; byte memory is wrapped, not copied, and must not change while the predicate is in use.</summary>
        public static UtxoPredicate Address(ReadOnlyMemory<byte> exactAddress = default, ReadOnlyMemory<byte> paymentPart = default, ReadOnlyMemory<byte> delegationPart = default) =>
            new() { Match = new() { Cardano = new() { Address = Predicates.V1BetaAddress(exactAddress, paymentPart, delegationPart) } } };

        /// <summary>Matches an output's asset; byte memory is wrapped, not copied, and must not change while the predicate is in use. An empty name means any name; for an asset whose name is empty, build the v1beta pattern with AssetName = ByteString.Empty (v1alpha cannot express it).</summary>
        public static UtxoPredicate Asset(ReadOnlyMemory<byte> policyId = default, ReadOnlyMemory<byte> assetName = default) =>
            new() { Match = new() { Cardano = new() { Asset = Predicates.V1BetaAsset(policyId, assetName) } } };

        /// <summary>Matches when all supplied predicates match.</summary>
        public static UtxoPredicate AllOf(params ReadOnlySpan<UtxoPredicate> predicates)
        {
            UtxoPredicate result = new();
            Predicates.Fill(result.AllOf, predicates, nameof(predicates));
            return result;
        }

        /// <summary>Matches when any supplied predicate matches.</summary>
        public static UtxoPredicate AnyOf(params ReadOnlySpan<UtxoPredicate> predicates)
        {
            UtxoPredicate result = new();
            Predicates.Fill(result.AnyOf, predicates, nameof(predicates));
            return result;
        }

        /// <summary>Matches when the supplied predicate does not match.</summary>
        public static UtxoPredicate Not(UtxoPredicate predicate)
        {
            UtxoPredicate result = new();
            Predicates.Fill(result.Not, [predicate], nameof(predicate));
            return result;
        }
    }
}
