using Google.Protobuf;
using Google.Protobuf.Collections;
using Alpha = Utxorpc.V1Alpha.Cardano;
using Beta = Utxorpc.V1Beta.Cardano;

namespace Utxorpc.Sdk.Internal;

internal static class Predicates
{
    private const string EmptyPartsMessage = "At least one non-empty part is required.";

    internal static void Fill<T>(RepeatedField<T> target, ReadOnlySpan<T> items, string parameterName)
    {
        if (items.IsEmpty) throw new ArgumentException("At least one predicate is required.", parameterName);
        target.Capacity = items.Length;
        for (int i = 0; i < items.Length; i++)
        {
            target.Add(items[i]);
        }
    }

    internal static ByteString Wrap(ReadOnlyMemory<byte> part) => part.IsEmpty ? ByteString.Empty : UnsafeByteOperations.UnsafeWrap(part);

    internal static ByteString Required(ReadOnlyMemory<byte> part, string parameterName) =>
        part.IsEmpty ? throw new ArgumentException(EmptyPartsMessage, parameterName) : UnsafeByteOperations.UnsafeWrap(part);

    internal static Alpha.AddressPattern V1AlphaAddress(ReadOnlyMemory<byte> exactAddress, ReadOnlyMemory<byte> paymentPart, ReadOnlyMemory<byte> delegationPart)
    {
        if (exactAddress.IsEmpty && paymentPart.IsEmpty && delegationPart.IsEmpty)
            throw new ArgumentException(EmptyPartsMessage, nameof(exactAddress) + ", " + nameof(paymentPart) + ", " + nameof(delegationPart));
        Alpha.AddressPattern result = new();
        if (!exactAddress.IsEmpty) result.ExactAddress = UnsafeByteOperations.UnsafeWrap(exactAddress);
        if (!paymentPart.IsEmpty) result.PaymentPart = UnsafeByteOperations.UnsafeWrap(paymentPart);
        if (!delegationPart.IsEmpty) result.DelegationPart = UnsafeByteOperations.UnsafeWrap(delegationPart);
        return result;
    }

    internal static Alpha.AssetPattern V1AlphaAsset(ReadOnlyMemory<byte> policyId, ReadOnlyMemory<byte> assetName)
    {
        if (policyId.IsEmpty && assetName.IsEmpty)
            throw new ArgumentException(EmptyPartsMessage, nameof(policyId) + ", " + nameof(assetName));
        Alpha.AssetPattern result = new();
        if (!policyId.IsEmpty) result.PolicyId = UnsafeByteOperations.UnsafeWrap(policyId);
        if (!assetName.IsEmpty) result.AssetName = UnsafeByteOperations.UnsafeWrap(assetName);
        return result;
    }

    internal static Alpha.TxOutputPattern V1AlphaOutput(ReadOnlyMemory<byte> exactAddress, ReadOnlyMemory<byte> paymentPart, ReadOnlyMemory<byte> delegationPart, ReadOnlyMemory<byte> policyId, ReadOnlyMemory<byte> assetName)
    {
        bool address = !exactAddress.IsEmpty || !paymentPart.IsEmpty || !delegationPart.IsEmpty;
        bool asset = !policyId.IsEmpty || !assetName.IsEmpty;
        if (!address && !asset)
            throw new ArgumentException(EmptyPartsMessage, nameof(exactAddress) + ", " + nameof(paymentPart) + ", " + nameof(delegationPart) + ", " + nameof(policyId) + ", " + nameof(assetName));
        Alpha.TxOutputPattern result = new();
        if (address) result.Address = V1AlphaAddress(exactAddress, paymentPart, delegationPart);
        if (asset) result.Asset = V1AlphaAsset(policyId, assetName);
        return result;
    }

    internal static Beta.AddressPattern V1BetaAddress(ReadOnlyMemory<byte> exactAddress, ReadOnlyMemory<byte> paymentPart, ReadOnlyMemory<byte> delegationPart)
    {
        if (exactAddress.IsEmpty && paymentPart.IsEmpty && delegationPart.IsEmpty)
            throw new ArgumentException(EmptyPartsMessage, nameof(exactAddress) + ", " + nameof(paymentPart) + ", " + nameof(delegationPart));
        Beta.AddressPattern result = new();
        if (!exactAddress.IsEmpty) result.ExactAddress = UnsafeByteOperations.UnsafeWrap(exactAddress);
        if (!paymentPart.IsEmpty) result.PaymentPart = UnsafeByteOperations.UnsafeWrap(paymentPart);
        if (!delegationPart.IsEmpty) result.DelegationPart = UnsafeByteOperations.UnsafeWrap(delegationPart);
        return result;
    }

    internal static Beta.AssetPattern V1BetaAsset(ReadOnlyMemory<byte> policyId, ReadOnlyMemory<byte> assetName)
    {
        if (policyId.IsEmpty && assetName.IsEmpty)
            throw new ArgumentException(EmptyPartsMessage, nameof(policyId) + ", " + nameof(assetName));
        Beta.AssetPattern result = new();
        if (!policyId.IsEmpty) result.PolicyId = UnsafeByteOperations.UnsafeWrap(policyId);
        if (!assetName.IsEmpty) result.AssetName = UnsafeByteOperations.UnsafeWrap(assetName);
        return result;
    }

    internal static Beta.TxOutputPattern V1BetaOutput(ReadOnlyMemory<byte> exactAddress, ReadOnlyMemory<byte> paymentPart, ReadOnlyMemory<byte> delegationPart, ReadOnlyMemory<byte> policyId, ReadOnlyMemory<byte> assetName)
    {
        bool address = !exactAddress.IsEmpty || !paymentPart.IsEmpty || !delegationPart.IsEmpty;
        bool asset = !policyId.IsEmpty || !assetName.IsEmpty;
        if (!address && !asset)
            throw new ArgumentException(EmptyPartsMessage, nameof(exactAddress) + ", " + nameof(paymentPart) + ", " + nameof(delegationPart) + ", " + nameof(policyId) + ", " + nameof(assetName));
        Beta.TxOutputPattern result = new();
        if (address) result.Address = V1BetaAddress(exactAddress, paymentPart, delegationPart);
        if (asset) result.Asset = V1BetaAsset(policyId, assetName);
        return result;
    }
}
