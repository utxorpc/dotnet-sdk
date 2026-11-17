using System.Runtime.CompilerServices;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Beta.Submit;
using WaitForTxCall = (Utxorpc.V1Beta.Submit.SubmitService.SubmitServiceClient Client, Utxorpc.V1Beta.Submit.WaitForTxRequest Request);
using WatchMempoolCall = (Utxorpc.V1Beta.Submit.SubmitService.SubmitServiceClient Client, Utxorpc.V1Beta.Submit.WatchMempoolRequest Request);

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1beta submission client.</summary>
public static class V1BetaSubmitExtensions
{
    /// <include file="Docs.xml" path="docs/member[@name='SubmitTxAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    public static Task<SubmitTxResponse> SubmitTxAsync(this SubmitService.SubmitServiceClient client, ReadOnlyMemory<byte> tx, CancellationToken cancellationToken = default) =>
        Errors.Typed(client.SubmitTxAsync(new SubmitTxRequest { Tx = new() { Raw = UnsafeByteOperations.UnsafeWrap(tx) } }, cancellationToken: cancellationToken).ResponseAsync, transaction: true);

    /// <include file="Docs.xml" path="docs/member[@name='WaitForTxAsync']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='WaitForTxAsync']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
    [OverloadResolutionPriority(1)]
    public static IAsyncEnumerable<WaitForTxResponse> WaitForTxAsync(this SubmitService.SubmitServiceClient client, ReadOnlyMemory<byte> txHash, CancellationToken cancellationToken = default) =>
        client.WaitForTxAsync(new ReadOnlySpan<ReadOnlyMemory<byte>>(in txHash), cancellationToken);

    /// <include file="Docs.xml" path="docs/member[@name='WaitForTxAsync(txHashes)']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='WaitForTxAsync(txHashes)']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
    public static IAsyncEnumerable<WaitForTxResponse> WaitForTxAsync(this SubmitService.SubmitServiceClient client, ReadOnlySpan<ReadOnlyMemory<byte>> txHashes, CancellationToken cancellationToken = default)
    {
        WaitForTxRequest request = new();
        request.Ref.Capacity = txHashes.Length;
        for (int i = 0; i < txHashes.Length; i++)
        {
            request.Ref.Add(UnsafeByteOperations.UnsafeWrap(txHashes[i]));
        }
        return ServerStream.ReadAllAsync<WaitForTxCall, WaitForTxResponse, WaitForTxResponse, WaitForTxStream>((client, request), cancellationToken);
    }

    /// <include file="Docs.xml" path="docs/member[@name='EvalTxAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    public static Task<EvalTxResponse> EvalTxAsync(this SubmitService.SubmitServiceClient client, ReadOnlyMemory<byte> tx, CancellationToken cancellationToken = default) =>
        Errors.Typed(client.EvalTxAsync(new EvalTxRequest { Tx = new() { Raw = UnsafeByteOperations.UnsafeWrap(tx) } }, cancellationToken: cancellationToken).ResponseAsync, transaction: true);

    /// <include file="Docs.xml" path="docs/member[@name='ReadMempoolAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    public static Task<ReadMempoolResponse> ReadMempoolAsync(this SubmitService.SubmitServiceClient client, CancellationToken cancellationToken = default) =>
        Errors.Typed(client.ReadMempoolAsync(new ReadMempoolRequest(), cancellationToken: cancellationToken).ResponseAsync);

    /// <include file="Docs.xml" path="docs/member[@name='WatchMempoolAsync']/*"/>
    /// <include file="Docs.xml" path="docs/stream/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static IAsyncEnumerable<WatchMempoolResponse> WatchMempoolAsync(this SubmitService.SubmitServiceClient client, TxPredicate? predicate = null, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        ServerStream.ReadAllAsync<WatchMempoolCall, WatchMempoolResponse, WatchMempoolResponse, WatchMempoolStream>(
            (client, new WatchMempoolRequest { Predicate = predicate, FieldMask = fieldMask }), cancellationToken);

    private readonly struct WaitForTxStream : IServerStream<WaitForTxCall, WaitForTxResponse, WaitForTxResponse>
    {
        public static AsyncServerStreamingCall<WaitForTxResponse> Start(WaitForTxCall call, CancellationToken cancellationToken) =>
            call.Client.WaitForTx(call.Request, cancellationToken: cancellationToken);

        public static WaitForTxResponse Map(WaitForTxResponse message) => message;
    }

    private readonly struct WatchMempoolStream : IServerStream<WatchMempoolCall, WatchMempoolResponse, WatchMempoolResponse>
    {
        public static AsyncServerStreamingCall<WatchMempoolResponse> Start(WatchMempoolCall call, CancellationToken cancellationToken) =>
            call.Client.WatchMempool(call.Request, cancellationToken: cancellationToken);

        public static WatchMempoolResponse Map(WatchMempoolResponse message) => message;
    }

    extension(TxPredicate)
    {
        /// <include file="Docs.xml" path="docs/member[@name='TxPredicate.HasAddress']/*"/>
        /// <include file="Docs.xml" path="docs/address/*"/>
        /// <include file="Docs.xml" path="docs/predicate/*[not(self::remarks)]"/>
        /// <remarks><include file="Docs.xml" path="docs/predicate/remarks/*"/> <include file="Docs.xml" path="docs/member[@name='oneOutput']/remarks/*"/></remarks>
        public static TxPredicate HasAddress(ReadOnlyMemory<byte> exactAddress = default, ReadOnlyMemory<byte> paymentPart = default, ReadOnlyMemory<byte> delegationPart = default) =>
            new() { Match = new() { Cardano = new() { HasAddress = Predicates.V1BetaAddress(exactAddress, paymentPart, delegationPart) } } };

        /// <include file="Docs.xml" path="docs/member[@name='TxPredicate.MovesAsset']/*"/>
        /// <include file="Docs.xml" path="docs/asset/*"/>
        /// <include file="Docs.xml" path="docs/predicate/*"/>
        public static TxPredicate MovesAsset(ReadOnlyMemory<byte> policyId = default, ReadOnlyMemory<byte> assetName = default) =>
            new() { Match = new() { Cardano = new() { MovesAsset = Predicates.V1BetaAsset(policyId, assetName) } } };

        /// <include file="Docs.xml" path="docs/member[@name='TxPredicate.Consumes']/*"/>
        /// <include file="Docs.xml" path="docs/address/*"/>
        /// <include file="Docs.xml" path="docs/asset/*"/>
        /// <include file="Docs.xml" path="docs/predicate/*[not(self::remarks)]"/>
        /// <remarks><include file="Docs.xml" path="docs/predicate/remarks/*"/> <include file="Docs.xml" path="docs/member[@name='oneOutput']/remarks/*"/></remarks>
        public static TxPredicate Consumes(ReadOnlyMemory<byte> exactAddress = default, ReadOnlyMemory<byte> paymentPart = default, ReadOnlyMemory<byte> delegationPart = default, ReadOnlyMemory<byte> policyId = default, ReadOnlyMemory<byte> assetName = default) =>
            new() { Match = new() { Cardano = new() { Consumes = Predicates.V1BetaOutput(exactAddress, paymentPart, delegationPart, policyId, assetName) } } };

        /// <include file="Docs.xml" path="docs/member[@name='TxPredicate.Produces']/*"/>
        /// <include file="Docs.xml" path="docs/address/*"/>
        /// <include file="Docs.xml" path="docs/asset/*"/>
        /// <include file="Docs.xml" path="docs/predicate/*[not(self::remarks)]"/>
        /// <remarks><include file="Docs.xml" path="docs/predicate/remarks/*"/> <include file="Docs.xml" path="docs/member[@name='oneOutput']/remarks/*"/></remarks>
        public static TxPredicate Produces(ReadOnlyMemory<byte> exactAddress = default, ReadOnlyMemory<byte> paymentPart = default, ReadOnlyMemory<byte> delegationPart = default, ReadOnlyMemory<byte> policyId = default, ReadOnlyMemory<byte> assetName = default) =>
            new() { Match = new() { Cardano = new() { Produces = Predicates.V1BetaOutput(exactAddress, paymentPart, delegationPart, policyId, assetName) } } };

        /// <include file="Docs.xml" path="docs/member[@name='TxPredicate.MintsAsset']/*"/>
        /// <include file="Docs.xml" path="docs/asset/*"/>
        /// <include file="Docs.xml" path="docs/predicate/*"/>
        public static TxPredicate MintsAsset(ReadOnlyMemory<byte> policyId = default, ReadOnlyMemory<byte> assetName = default) =>
            new() { Match = new() { Cardano = new() { MintsAsset = Predicates.V1BetaAsset(policyId, assetName) } } };

        /// <include file="Docs.xml" path="docs/member[@name='TxPredicate.HasCertificate']/*"/>
        public static TxPredicate HasCertificate(Utxorpc.V1Beta.Cardano.CertificatePattern pattern) =>
            new() { Match = new() { Cardano = new() { HasCertificate = pattern ?? throw new ArgumentNullException(nameof(pattern)) } } };

        /// <include file="Docs.xml" path="docs/member[@name='AllOf']/*"/>
        public static TxPredicate AllOf(params ReadOnlySpan<TxPredicate> predicates)
        {
            TxPredicate result = new();
            Predicates.Fill(result.AllOf, predicates, nameof(predicates));
            return result;
        }

        /// <include file="Docs.xml" path="docs/member[@name='AnyOf']/*"/>
        public static TxPredicate AnyOf(params ReadOnlySpan<TxPredicate> predicates)
        {
            TxPredicate result = new();
            Predicates.Fill(result.AnyOf, predicates, nameof(predicates));
            return result;
        }

        /// <include file="Docs.xml" path="docs/member[@name='Not']/*"/>
        public static TxPredicate Not(TxPredicate predicate)
        {
            TxPredicate result = new();
            Predicates.Fill(result.Not, [predicate], nameof(predicate));
            return result;
        }
    }
}
