using System.Runtime.CompilerServices;
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
    /// <include file="Docs.xml" path="docs/member[@name='WatchTxAsync']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='WatchTxAsync']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
    [OverloadResolutionPriority(1)]
    public static IAsyncEnumerable<WatchTxEvent> WatchTxAsync(this WatchService.WatchServiceClient client, TxPredicate? predicate = null, BlockPoint? from = null, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.WatchTxAsync(predicate, from is BlockPoint point ? new ReadOnlySpan<BlockPoint>(in point) : default, fieldMask, cancellationToken);

    /// <include file="Docs.xml" path="docs/member[@name='WatchTxAsync(intersect)']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='WatchTxAsync(intersect)']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
    public static IAsyncEnumerable<WatchTxEvent> WatchTxAsync(this WatchService.WatchServiceClient client, TxPredicate? predicate, ReadOnlySpan<BlockPoint> intersect, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        WatchTxRequest request = new() { Predicate = predicate, FieldMask = fieldMask };
        request.Intersect.Capacity = intersect.Length;
        for (int i = 0; i < intersect.Length; i++)
        {
            request.Intersect.Add(new BlockRef { Slot = intersect[i].Slot, Hash = UnsafeByteOperations.UnsafeWrap(intersect[i].Hash) });
        }
        return StreamResume.For(client) is ResumeSettings resume
            ? ServerStream.ReadAllResumingAsync<WatchTxCall, WatchTxResponse, WatchTxEvent, WatchTxEvent>((client, request), resume, cancellationToken)
            : ServerStream.ReadAllAsync<WatchTxCall, WatchTxResponse, WatchTxEvent, WatchTxEvent>((client, request), cancellationToken);
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
