using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Beta.Query;

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1beta query client.</summary>
public static class V1BetaQueryExtensions
{
    /// <include file="Docs.xml" path="docs/member[@name='ReadParamsAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static Task<ReadParamsResponse> ReadParamsAsync(this QueryService.QueryServiceClient client, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        Errors.Typed(client.ReadParamsAsync(new ReadParamsRequest { FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync);

    /// <include file="Docs.xml" path="docs/member[@name='ReadUtxosAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static Task<ReadUtxosResponse> ReadUtxosAsync(this QueryService.QueryServiceClient client, ReadOnlySpan<OutputReference> references, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        ReadUtxosRequest request = new() { FieldMask = fieldMask };
        request.Keys.Capacity = references.Length;
        for (int i = 0; i < references.Length; i++)
        {
            request.Keys.Add(new TxoRef { Hash = UnsafeByteOperations.UnsafeWrap(references[i].TxHash), Index = references[i].Index });
        }
        return Errors.Typed(client.ReadUtxosAsync(request, cancellationToken: cancellationToken).ResponseAsync);
    }

    /// <include file="Docs.xml" path="docs/member[@name='SearchAllUtxosAsync']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='SearchAllUtxosAsync']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
    public static IAsyncEnumerable<AnyUtxoData> SearchAllUtxosAsync(this QueryService.QueryServiceClient client, UtxoPredicate predicate, int pageSize = 0, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageSize);
        return Paging.ReadAllPagesAsync<QueryService.QueryServiceClient, (UtxoPredicate Predicate, int PageSize, FieldMask? Mask), string, SearchUtxosResponse, AnyUtxoData>(
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
    }

    /// <include file="Docs.xml" path="docs/member[@name='SearchUtxosByAddressAsync']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='SearchUtxosByAddressAsync']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
    public static IAsyncEnumerable<AnyUtxoData> SearchUtxosByAddressAsync(this QueryService.QueryServiceClient client, ReadOnlyMemory<byte> address, int pageSize = 0, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.SearchAllUtxosAsync(UtxoPredicate.Address(exactAddress: address), pageSize, fieldMask, cancellationToken);

    /// <include file="Docs.xml" path="docs/member[@name='ReadTxAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static Task<ReadTxResponse> ReadTxAsync(this QueryService.QueryServiceClient client, ReadOnlyMemory<byte> txHash, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        Errors.Typed(client.ReadTxAsync(new ReadTxRequest { Hash = UnsafeByteOperations.UnsafeWrap(txHash), FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync);

    /// <include file="Docs.xml" path="docs/member[@name='ReadStateAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static Task<ReadStateResponse> ReadStateAsync(this QueryService.QueryServiceClient client, Utxorpc.V1Beta.Cardano.StateQuery query, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        Errors.Typed(client.ReadStateAsync(new ReadStateRequest { Query = new AnyChainStateQuery { Cardano = query }, FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync);

    /// <include file="Docs.xml" path="docs/member[@name='ReadGenesisAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static Task<ReadGenesisResponse> ReadGenesisAsync(this QueryService.QueryServiceClient client, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        Errors.Typed(client.ReadGenesisAsync(new ReadGenesisRequest { FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync);

    /// <include file="Docs.xml" path="docs/member[@name='ReadEraSummaryAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static Task<ReadEraSummaryResponse> ReadEraSummaryAsync(this QueryService.QueryServiceClient client, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        Errors.Typed(client.ReadEraSummaryAsync(new ReadEraSummaryRequest { FieldMask = fieldMask }, cancellationToken: cancellationToken).ResponseAsync);

    /// <include file="Docs.xml" path="docs/member[@name='ReadDataAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static Task<ReadDataResponse> ReadDataAsync(this QueryService.QueryServiceClient client, ReadOnlySpan<ReadOnlyMemory<byte>> datumHashes, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        ReadDataRequest request = new() { FieldMask = fieldMask };
        request.Keys.Capacity = datumHashes.Length;
        for (int i = 0; i < datumHashes.Length; i++)
        {
            request.Keys.Add(UnsafeByteOperations.UnsafeWrap(datumHashes[i]));
        }
        return Errors.Typed(client.ReadDataAsync(request, cancellationToken: cancellationToken).ResponseAsync);
    }

    extension(UtxoPredicate)
    {
        /// <include file="Docs.xml" path="docs/member[@name='UtxoPredicate.Match']/*"/>
        /// <include file="Docs.xml" path="docs/address/*"/>
        /// <include file="Docs.xml" path="docs/asset/*"/>
        /// <include file="Docs.xml" path="docs/predicate/*"/>
        public static UtxoPredicate Match(ReadOnlyMemory<byte> exactAddress = default, ReadOnlyMemory<byte> paymentPart = default, ReadOnlyMemory<byte> delegationPart = default, ReadOnlyMemory<byte> policyId = default, ReadOnlyMemory<byte> assetName = default) =>
            new() { Match = new() { Cardano = Predicates.V1BetaOutput(exactAddress, paymentPart, delegationPart, policyId, assetName) } };

        /// <include file="Docs.xml" path="docs/member[@name='UtxoPredicate.Address']/*"/>
        /// <include file="Docs.xml" path="docs/address/*"/>
        /// <include file="Docs.xml" path="docs/predicate/*"/>
        public static UtxoPredicate Address(ReadOnlyMemory<byte> exactAddress = default, ReadOnlyMemory<byte> paymentPart = default, ReadOnlyMemory<byte> delegationPart = default) =>
            new() { Match = new() { Cardano = new() { Address = Predicates.V1BetaAddress(exactAddress, paymentPart, delegationPart) } } };

        /// <include file="Docs.xml" path="docs/member[@name='UtxoPredicate.Asset']/*"/>
        /// <include file="Docs.xml" path="docs/asset/*"/>
        /// <include file="Docs.xml" path="docs/predicate/*"/>
        public static UtxoPredicate Asset(ReadOnlyMemory<byte> policyId = default, ReadOnlyMemory<byte> assetName = default) =>
            new() { Match = new() { Cardano = new() { Asset = Predicates.V1BetaAsset(policyId, assetName) } } };

        /// <include file="Docs.xml" path="docs/member[@name='AllOf']/*"/>
        public static UtxoPredicate AllOf(params ReadOnlySpan<UtxoPredicate> predicates)
        {
            UtxoPredicate result = new();
            Predicates.Fill(result.AllOf, predicates, nameof(predicates));
            return result;
        }

        /// <include file="Docs.xml" path="docs/member[@name='AnyOf']/*"/>
        public static UtxoPredicate AnyOf(params ReadOnlySpan<UtxoPredicate> predicates)
        {
            UtxoPredicate result = new();
            Predicates.Fill(result.AnyOf, predicates, nameof(predicates));
            return result;
        }

        /// <include file="Docs.xml" path="docs/member[@name='Not']/*"/>
        public static UtxoPredicate Not(UtxoPredicate predicate)
        {
            UtxoPredicate result = new();
            Predicates.Fill(result.Not, [predicate], nameof(predicate));
            return result;
        }
    }
}
