using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Test.Fakes;
using Xunit.Abstractions;
using AlphaQ = Utxorpc.V1Alpha.Query;
using AlphaS = Utxorpc.V1Alpha.Submit;
using AlphaW = Utxorpc.V1Alpha.Watch;
using BetaQ = Utxorpc.V1Beta.Query;
using BetaS = Utxorpc.V1Beta.Submit;
using BetaW = Utxorpc.V1Beta.Watch;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks predicate messages, wire bytes, validation, transport and allocation costs.</summary>
[Trait("Category", "Unit")]
public sealed class PredicateBuilderTests(ITestOutputHelper output)
{
    /// <summary>Builds address and asset fields exactly, preserving optional-field presence.</summary>
    [Fact]
    public void UtxoAddressAndAssetBuildTheSpecNesting()
    {
        byte[] p = [1, 2];
        byte[] q = [3, 4];
        byte[] n = [5, 6];
        BetaQ.UtxoPredicate expectedAddress = new() { Match = new() { Cardano = new() { Address = new() { PaymentPart = ByteString.CopyFrom(p) } } } };
        BetaQ.UtxoPredicate address = BetaQ.UtxoPredicate.Address(paymentPart: p);
        Assert.Equal(expectedAddress, address);
        Assert.False(address.Match.Cardano.Address.HasExactAddress);
        Assert.False(address.Match.Cardano.Address.HasDelegationPart);
        Assert.Equal(expectedAddress.ToByteString(), AlphaQ.UtxoPredicate.Address(paymentPart: p).ToByteString());
        BetaQ.UtxoPredicate expectedParts = new() { Match = new() { Cardano = new() { Address = new() { ExactAddress = ByteString.CopyFrom(p), PaymentPart = ByteString.CopyFrom(q), DelegationPart = ByteString.CopyFrom(n) } } } };
        Assert.Equal(expectedParts, BetaQ.UtxoPredicate.Address(p, q, n));
        Assert.Equal(expectedParts.ToByteString(), AlphaQ.UtxoPredicate.Address(p, q, n).ToByteString());
        BetaQ.UtxoPredicate expectedAsset = new() { Match = new() { Cardano = new() { Asset = new() { PolicyId = ByteString.CopyFrom(q), AssetName = ByteString.CopyFrom(n) } } } };
        Assert.Equal(expectedAsset, BetaQ.UtxoPredicate.Asset(policyId: q, assetName: n));
        Assert.Equal(expectedAsset.ToByteString(), AlphaQ.UtxoPredicate.Asset(policyId: q, assetName: n).ToByteString());
        BetaQ.UtxoPredicate expectedPolicy = new() { Match = new() { Cardano = new() { Asset = new() { PolicyId = ByteString.CopyFrom(q) } } } };
        BetaQ.UtxoPredicate policy = BetaQ.UtxoPredicate.Asset(policyId: q);
        Assert.Equal(expectedPolicy, policy);
        Assert.False(policy.Match.Cardano.Asset.HasAssetName);
        Assert.Equal(expectedPolicy.ToByteString(), AlphaQ.UtxoPredicate.Asset(policyId: q).ToByteString());
        BetaQ.UtxoPredicate expectedName = new() { Match = new() { Cardano = new() { Asset = new() { AssetName = ByteString.CopyFrom(n) } } } };
        BetaQ.UtxoPredicate name = BetaQ.UtxoPredicate.Asset(assetName: n);
        Assert.Equal(expectedName, name);
        Assert.False(name.Match.Cardano.Asset.HasPolicyId);
        Assert.Equal(expectedName.ToByteString(), AlphaQ.UtxoPredicate.Asset(assetName: n).ToByteString());
    }

    /// <summary>Builds exact transaction-address and asset matches in both services and versions.</summary>
    [Fact]
    public void TxHasAddressAndMovesAssetBuildTheSpecNesting()
    {
        byte[] a = [7, 8];
        byte[] q = [9, 10];
        BetaW.TxPredicate expectedAddress = new() { Match = new() { Cardano = new() { HasAddress = new() { ExactAddress = ByteString.CopyFrom(a) } } } };
        IMessage[] addresses = [AlphaW.TxPredicate.HasAddress(exactAddress: a), BetaW.TxPredicate.HasAddress(exactAddress: a),
            AlphaS.TxPredicate.HasAddress(exactAddress: a), BetaS.TxPredicate.HasAddress(exactAddress: a)];
        foreach (IMessage address in addresses)
        {
            Assert.Equal(address.Descriptor.Parser.ParseFrom(expectedAddress.ToByteString()), address);
            Assert.Equal(expectedAddress.ToByteString(), address.ToByteString());
        }
        BetaW.TxPredicate expectedAsset = new() { Match = new() { Cardano = new() { MovesAsset = new() { PolicyId = ByteString.CopyFrom(q) } } } };
        IMessage[] assets = [AlphaW.TxPredicate.MovesAsset(policyId: q), BetaW.TxPredicate.MovesAsset(policyId: q),
            AlphaS.TxPredicate.MovesAsset(policyId: q), BetaS.TxPredicate.MovesAsset(policyId: q)];
        foreach (IMessage asset in assets)
        {
            Assert.Equal(asset.Descriptor.Parser.ParseFrom(expectedAsset.ToByteString()), asset);
            Assert.Equal(expectedAsset.ToByteString(), asset.ToByteString());
        }
    }

    /// <summary>Keeps every combinator's field and input order for all six predicate types.</summary>
    [Fact]
    public void CombinatorsKeepOrder()
    {
        byte[] p = [11, 12];
        byte[] q = [13, 14];
        BetaQ.UtxoPredicate qa = BetaQ.UtxoPredicate.Address(paymentPart: p);
        BetaQ.UtxoPredicate qb = BetaQ.UtxoPredicate.Asset(policyId: q);
        AlphaQ.UtxoPredicate aqa = AlphaQ.UtxoPredicate.Parser.ParseFrom(qa.ToByteString());
        AlphaQ.UtxoPredicate aqb = AlphaQ.UtxoPredicate.Parser.ParseFrom(qb.ToByteString());
        BetaW.TxPredicate wa = BetaW.TxPredicate.HasAddress(exactAddress: p);
        BetaW.TxPredicate wb = BetaW.TxPredicate.MovesAsset(policyId: q);
        AlphaW.TxPredicate awa = AlphaW.TxPredicate.Parser.ParseFrom(wa.ToByteString());
        AlphaW.TxPredicate awb = AlphaW.TxPredicate.Parser.ParseFrom(wb.ToByteString());
        BetaS.TxPredicate sa = BetaS.TxPredicate.Parser.ParseFrom(wa.ToByteString());
        BetaS.TxPredicate sb = BetaS.TxPredicate.Parser.ParseFrom(wb.ToByteString());
        AlphaS.TxPredicate asa = AlphaS.TxPredicate.Parser.ParseFrom(wa.ToByteString());
        AlphaS.TxPredicate asb = AlphaS.TxPredicate.Parser.ParseFrom(wb.ToByteString());
        BetaQ.UtxoPredicate allQ = new() { AllOf = { qa, qb } };
        BetaQ.UtxoPredicate anyQ = new() { AnyOf = { qa, qb } };
        BetaQ.UtxoPredicate notQ = new() { Not = { qa } };
        BetaW.TxPredicate allW = new() { AllOf = { wa, wb } };
        BetaW.TxPredicate anyW = new() { AnyOf = { wa, wb } };
        BetaW.TxPredicate notW = new() { Not = { wa } };
        (IMessage Actual, IMessage Expected)[] cases = [
            (BetaQ.UtxoPredicate.AllOf(qa, qb), allQ), (BetaQ.UtxoPredicate.AnyOf(qa, qb), anyQ), (BetaQ.UtxoPredicate.Not(qa), notQ),
            (AlphaQ.UtxoPredicate.AllOf(aqa, aqb), allQ), (AlphaQ.UtxoPredicate.AnyOf(aqa, aqb), anyQ), (AlphaQ.UtxoPredicate.Not(aqa), notQ),
            (BetaW.TxPredicate.AllOf(wa, wb), allW), (BetaW.TxPredicate.AnyOf(wa, wb), anyW), (BetaW.TxPredicate.Not(wa), notW),
            (AlphaW.TxPredicate.AllOf(awa, awb), allW), (AlphaW.TxPredicate.AnyOf(awa, awb), anyW), (AlphaW.TxPredicate.Not(awa), notW),
            (BetaS.TxPredicate.AllOf(sa, sb), allW), (BetaS.TxPredicate.AnyOf(sa, sb), anyW), (BetaS.TxPredicate.Not(sa), notW),
            (AlphaS.TxPredicate.AllOf(asa, asb), allW), (AlphaS.TxPredicate.AnyOf(asa, asb), anyW), (AlphaS.TxPredicate.Not(asa), notW)];
        foreach ((IMessage actual, IMessage expected) in cases)
        {
            Assert.Equal(actual.Descriptor.Parser.ParseFrom(expected.ToByteString()), actual);
            Assert.Equal(expected.ToByteString(), actual.ToByteString());
        }
    }

    /// <summary>Rejects empty patterns, empty conjunctions and null predicate elements.</summary>
    [Fact]
    public void EmptyInputThrows()
    {
        Assert.ThrowsAny<ArgumentException>(() => AlphaQ.UtxoPredicate.Address());
        Assert.ThrowsAny<ArgumentException>(() => BetaQ.UtxoPredicate.Address());
        Assert.ThrowsAny<ArgumentException>(() => AlphaQ.UtxoPredicate.Asset());
        Assert.ThrowsAny<ArgumentException>(() => BetaQ.UtxoPredicate.Asset());
        Assert.Equal("predicates", Assert.ThrowsAny<ArgumentException>(() => AlphaQ.UtxoPredicate.AllOf()).ParamName);
        Assert.Equal("predicates", Assert.ThrowsAny<ArgumentException>(() => BetaQ.UtxoPredicate.AllOf()).ParamName);
        Assert.ThrowsAny<ArgumentException>(() => AlphaW.TxPredicate.HasAddress());
        Assert.ThrowsAny<ArgumentException>(() => BetaW.TxPredicate.HasAddress());
        Assert.ThrowsAny<ArgumentException>(() => AlphaS.TxPredicate.MovesAsset());
        Assert.ThrowsAny<ArgumentException>(() => BetaS.TxPredicate.MovesAsset());
        Assert.ThrowsAny<ArgumentException>(() => AlphaQ.UtxoPredicate.Not(null!));
        Assert.ThrowsAny<ArgumentException>(() => BetaQ.UtxoPredicate.Not(null!));
    }

    /// <summary>Allocates no more than equivalent manually constructed messages.</summary>
    [Fact]
    public void BuilderAllocatesLikeTheHandBuiltMessage()
    {
        ReadOnlyMemory<byte> p = new byte[] { 15, 16 };
        BetaQ.UtxoPredicate a = BetaQ.UtxoPredicate.Address(paymentPart: p);
        BetaQ.UtxoPredicate b = BetaQ.UtxoPredicate.Address(exactAddress: p);
        for (int i = 0; i < 1000; i++)
        {
            _ = BetaQ.UtxoPredicate.AllOf(a, b);
            _ = ManualAllOf(a, b);
            _ = BetaQ.UtxoPredicate.Address(paymentPart: p);
            _ = ManualAddress(p);
        }
        long t0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = BetaQ.UtxoPredicate.AllOf(a, b);
        long t1 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = ManualAllOf(a, b);
        long t2 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = BetaQ.UtxoPredicate.Address(paymentPart: p);
        long t3 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = ManualAddress(p);
        long t4 = GC.GetAllocatedBytesForCurrentThread();
        output.WriteLine($"AllOf builder={t1 - t0}, manual={t2 - t1}; Address builder={t3 - t2}, manual={t4 - t3} bytes for 1000 calls.");
        Assert.True(t1 - t0 == t2 - t1, $"AllOf builder allocated {t1 - t0} bytes; hand-built allocated {t2 - t1} bytes.");
        Assert.True(t3 - t2 == t4 - t3, $"Address builder allocated {t3 - t2} bytes; hand-built allocated {t4 - t3} bytes.");

        static BetaQ.UtxoPredicate ManualAllOf(BetaQ.UtxoPredicate a, BetaQ.UtxoPredicate b)
        {
            BetaQ.UtxoPredicate result = new();
            result.AllOf.Capacity = 2;
            result.AllOf.Add(a);
            result.AllOf.Add(b);
            return result;
        }

        static BetaQ.UtxoPredicate ManualAddress(ReadOnlyMemory<byte> paymentPart) =>
            new() { Match = new() { Cardano = new() { Address = new() { PaymentPart = UnsafeByteOperations.UnsafeWrap(paymentPart) } } } };
    }

    /// <summary>Transmits independently specified predicate messages and masks without changes.</summary>
    [Fact]
    public async Task BuiltPredicatesReachTheServer()
    {
        await using FakeServer server = await FakeServer.StartAsync();
        using UtxorpcClient client = new(server.Url);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        byte[] p = [17, 18];
        byte[] q = [19, 20];
        FieldMask mask = FieldMask.FromString("native_bytes");
        BetaQ.UtxoPredicate expected = new()
        {
            AllOf = {
            new BetaQ.UtxoPredicate { Match = new() { Cardano = new() { Address = new() { PaymentPart = ByteString.CopyFrom(p) } } } },
            new BetaQ.UtxoPredicate { Match = new() { Cardano = new() { Asset = new() { PolicyId = ByteString.CopyFrom(q) } } } } }
        };
        AlphaQ.SearchUtxosRequest expectedAlpha = new() { Predicate = AlphaQ.UtxoPredicate.Parser.ParseFrom(expected.ToByteString()), FieldMask = mask };
        BetaQ.SearchUtxosRequest expectedBeta = new() { Predicate = expected, FieldMask = mask };
        await foreach (AlphaQ.AnyUtxoData _ in client.V1Alpha.Query.SearchAllUtxosAsync(
            AlphaQ.UtxoPredicate.AllOf(AlphaQ.UtxoPredicate.Address(paymentPart: p), AlphaQ.UtxoPredicate.Asset(policyId: q)),
            fieldMask: mask, cancellationToken: cancellation.Token)) break;
        await foreach (BetaQ.AnyUtxoData _ in client.V1Beta.Query.SearchAllUtxosAsync(
            BetaQ.UtxoPredicate.AllOf(BetaQ.UtxoPredicate.Address(paymentPart: p), BetaQ.UtxoPredicate.Asset(policyId: q)),
            fieldMask: mask, cancellationToken: cancellation.Token)) break;
        Assert.True(server.State.Requests.TryDequeue(out IMessage? first));
        Assert.True(server.State.Requests.TryDequeue(out IMessage? second));
        AlphaQ.SearchUtxosRequest actualAlpha = Assert.IsType<AlphaQ.SearchUtxosRequest>(first);
        BetaQ.SearchUtxosRequest actualBeta = Assert.IsType<BetaQ.SearchUtxosRequest>(second);
        Assert.Equal(expectedAlpha, actualAlpha);
        Assert.Equal(expectedAlpha.ToByteString(), actualAlpha.ToByteString());
        Assert.Equal(expectedBeta, actualBeta);
        Assert.Equal(expectedBeta.ToByteString(), actualBeta.ToByteString());
        Assert.Empty(server.State.Requests);
    }
}
