using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Test.Fakes;
using Xunit.Abstractions;
using AlphaC = Utxorpc.V1Alpha.Cardano;
using AlphaQ = Utxorpc.V1Alpha.Query;
using AlphaS = Utxorpc.V1Alpha.Submit;
using AlphaW = Utxorpc.V1Alpha.Watch;
using BetaC = Utxorpc.V1Beta.Cardano;
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

    /// <summary>Builds input, output and mint patterns on both transaction services and versions.</summary>
    [Fact]
    public void TxConsumesProducesAndMintsBuildTheSpecNesting()
    {
        byte[] a = [1, 2], q = [3, 4], n = [5, 6];
        BetaC.TxOutputPattern both = new() { Address = new() { PaymentPart = ByteString.CopyFrom(a) }, Asset = new() { PolicyId = ByteString.CopyFrom(q), AssetName = ByteString.CopyFrom(n) } };
        BetaW.TxPredicate consumes = new() { Match = new() { Cardano = new() { Consumes = both } } };
        BetaW.TxPredicate produces = new() { Match = new() { Cardano = new() { Produces = both } } };
        BetaW.TxPredicate mints = new() { Match = new() { Cardano = new() { MintsAsset = new() { PolicyId = ByteString.CopyFrom(q) } } } };
        (IMessage Actual, IMessage Expected)[] cases = [
            (BetaW.TxPredicate.Consumes(paymentPart: a, policyId: q, assetName: n), consumes), (AlphaW.TxPredicate.Consumes(paymentPart: a, policyId: q, assetName: n), consumes),
            (BetaS.TxPredicate.Consumes(paymentPart: a, policyId: q, assetName: n), consumes), (AlphaS.TxPredicate.Consumes(paymentPart: a, policyId: q, assetName: n), consumes),
            (BetaW.TxPredicate.Produces(paymentPart: a, policyId: q, assetName: n), produces), (AlphaW.TxPredicate.Produces(paymentPart: a, policyId: q, assetName: n), produces),
            (BetaS.TxPredicate.Produces(paymentPart: a, policyId: q, assetName: n), produces), (AlphaS.TxPredicate.Produces(paymentPart: a, policyId: q, assetName: n), produces),
            (BetaW.TxPredicate.MintsAsset(policyId: q), mints), (AlphaW.TxPredicate.MintsAsset(policyId: q), mints),
            (BetaS.TxPredicate.MintsAsset(policyId: q), mints), (AlphaS.TxPredicate.MintsAsset(policyId: q), mints)];
        foreach ((IMessage actual, IMessage expected) in cases)
        {
            Assert.Equal(actual.Descriptor.Parser.ParseFrom(expected.ToByteString()), actual);
            Assert.Equal(expected.ToByteString(), actual.ToByteString());
        }
        Assert.Null(BetaW.TxPredicate.Produces(exactAddress: a).Match.Cardano.Produces.Asset);
        Assert.Null(BetaW.TxPredicate.Consumes(policyId: q).Match.Cardano.Consumes.Address);
    }

    /// <summary>Builds every certificate kind with bare and constrained forms in both versions.</summary>
    [Fact]
    public void CertificatePatternsBuildEveryKind()
    {
        byte[] k = [7, 8], pool = [9, 10];
        (BetaC.CertificatePattern Actual, BetaC.CertificatePattern Expected, AlphaC.CertificatePattern Alpha)[] cases = [
            (BetaC.CertificatePattern.StakeRegistration(), new() { StakeRegistration = new() }, AlphaC.CertificatePattern.StakeRegistration()),
            (BetaC.CertificatePattern.StakeRegistration(addrKeyHash: k), new() { StakeRegistration = new() { AddrKeyHash = ByteString.CopyFrom(k) } }, AlphaC.CertificatePattern.StakeRegistration(addrKeyHash: k)),
            (BetaC.CertificatePattern.StakeDeregistration(scriptHash: k), new() { StakeDeregistration = new() { ScriptHash = ByteString.CopyFrom(k) } }, AlphaC.CertificatePattern.StakeDeregistration(scriptHash: k)),
            (BetaC.CertificatePattern.StakeDelegation(), new() { StakeDelegation = new() }, AlphaC.CertificatePattern.StakeDelegation()),
            (BetaC.CertificatePattern.StakeDelegation(addrKeyHash: k, poolKeyhash: pool), new() { StakeDelegation = new() { StakeCredential = new() { AddrKeyHash = ByteString.CopyFrom(k) }, PoolKeyhash = ByteString.CopyFrom(pool) } }, AlphaC.CertificatePattern.StakeDelegation(addrKeyHash: k, poolKeyhash: pool)),
            (BetaC.CertificatePattern.StakeDelegation(poolKeyhash: pool), new() { StakeDelegation = new() { PoolKeyhash = ByteString.CopyFrom(pool) } }, AlphaC.CertificatePattern.StakeDelegation(poolKeyhash: pool)),
            (BetaC.CertificatePattern.PoolRegistration(), new() { PoolRegistration = new() }, AlphaC.CertificatePattern.PoolRegistration()),
            (BetaC.CertificatePattern.PoolRegistration(poolOperator: pool), new() { PoolRegistration = new() { Operator = ByteString.CopyFrom(pool) } }, AlphaC.CertificatePattern.PoolRegistration(poolOperator: pool)),
            (BetaC.CertificatePattern.PoolRegistration(poolKeyhash: pool), new() { PoolRegistration = new() { PoolKeyhash = ByteString.CopyFrom(pool) } }, AlphaC.CertificatePattern.PoolRegistration(poolKeyhash: pool)),
            (BetaC.CertificatePattern.PoolRetirement(), new() { PoolRetirement = new() }, AlphaC.CertificatePattern.PoolRetirement()),
            (BetaC.CertificatePattern.PoolRetirement(pool, 500), new() { PoolRetirement = new() { PoolKeyhash = ByteString.CopyFrom(pool), Epoch = 500 } }, AlphaC.CertificatePattern.PoolRetirement(pool, 500)),
            (BetaC.CertificatePattern.AnyStakeCredential(k), new() { AnyStakeCredential = ByteString.CopyFrom(k) }, AlphaC.CertificatePattern.AnyStakeCredential(k)),
            (BetaC.CertificatePattern.AnyPoolKeyhash(pool), new() { AnyPoolKeyhash = ByteString.CopyFrom(pool) }, AlphaC.CertificatePattern.AnyPoolKeyhash(pool)),
            (BetaC.CertificatePattern.AnyDrep(k), new() { AnyDrep = ByteString.CopyFrom(k) }, AlphaC.CertificatePattern.AnyDrep(k))];
        foreach ((BetaC.CertificatePattern actual, BetaC.CertificatePattern expected, AlphaC.CertificatePattern alpha) in cases)
        {
            Assert.Equal(expected, actual);
            Assert.Equal(expected.CertificateTypeCase, actual.CertificateTypeCase);
            Assert.NotEqual(BetaC.CertificatePattern.CertificateTypeOneofCase.None, actual.CertificateTypeCase);
            Assert.Equal(expected.ToByteString(), actual.ToByteString());
            Assert.Equal(expected.ToByteString(), alpha.ToByteString());
            BetaW.TxPredicate predicate = BetaW.TxPredicate.HasCertificate(actual);
            Assert.Equal(new BetaW.TxPredicate { Match = new() { Cardano = new() { HasCertificate = expected } } }, predicate);
            Assert.Same(actual, predicate.Match.Cardano.HasCertificate);
            Assert.Same(actual, BetaS.TxPredicate.HasCertificate(actual).Match.Cardano.HasCertificate);
            Assert.Same(alpha, AlphaW.TxPredicate.HasCertificate(alpha).Match.Cardano.HasCertificate);
            Assert.Same(alpha, AlphaS.TxPredicate.HasCertificate(alpha).Match.Cardano.HasCertificate);
            Assert.Equal(predicate.ToByteString(), BetaS.TxPredicate.HasCertificate(actual).ToByteString());
            Assert.Equal(predicate.ToByteString(), AlphaW.TxPredicate.HasCertificate(alpha).ToByteString());
            Assert.Equal(predicate.ToByteString(), AlphaS.TxPredicate.HasCertificate(alpha).ToByteString());
        }
        Assert.Null(BetaC.CertificatePattern.StakeDelegation(poolKeyhash: pool).StakeDelegation.StakeCredential);
        Assert.Null(AlphaC.CertificatePattern.StakeDelegation(poolKeyhash: pool).StakeDelegation.StakeCredential);
    }

    /// <summary>Builds one output match without throwing away an address or asset node.</summary>
    [Fact]
    public void OneMatchBuildsAddressAndAssetTogether()
    {
        byte[] a = [1, 2], q = [3, 4], n = [5, 6];
        BetaQ.UtxoPredicate expected = new() { Match = new() { Cardano = new() { Address = new() { PaymentPart = ByteString.CopyFrom(a) }, Asset = new() { PolicyId = ByteString.CopyFrom(q), AssetName = ByteString.CopyFrom(n) } } } };
        Assert.Equal(expected, BetaQ.UtxoPredicate.Match(paymentPart: a, policyId: q, assetName: n));
        Assert.Equal(expected.ToByteString(), AlphaQ.UtxoPredicate.Match(paymentPart: a, policyId: q, assetName: n).ToByteString());
        Assert.Null(BetaQ.UtxoPredicate.Match(exactAddress: a).Match.Cardano.Asset);
        Assert.Null(BetaQ.UtxoPredicate.Match(policyId: q).Match.Cardano.Address);
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
        byte[] k = [1, 2];
        Action[] empty = [
            () => AlphaQ.UtxoPredicate.Match(), () => BetaQ.UtxoPredicate.Match(),
            () => AlphaW.TxPredicate.Consumes(), () => BetaW.TxPredicate.Consumes(), () => AlphaS.TxPredicate.Consumes(), () => BetaS.TxPredicate.Consumes(),
            () => AlphaW.TxPredicate.Produces(), () => BetaW.TxPredicate.Produces(), () => AlphaS.TxPredicate.Produces(), () => BetaS.TxPredicate.Produces(),
            () => AlphaW.TxPredicate.MintsAsset(), () => BetaW.TxPredicate.MintsAsset(), () => AlphaS.TxPredicate.MintsAsset(), () => BetaS.TxPredicate.MintsAsset(),
            () => AlphaW.TxPredicate.HasCertificate(null!), () => BetaW.TxPredicate.HasCertificate(null!), () => AlphaS.TxPredicate.HasCertificate(null!), () => BetaS.TxPredicate.HasCertificate(null!),
            () => AlphaC.CertificatePattern.AnyStakeCredential(default), () => BetaC.CertificatePattern.AnyStakeCredential(default),
            () => AlphaC.CertificatePattern.AnyPoolKeyhash(default), () => BetaC.CertificatePattern.AnyPoolKeyhash(default),
            () => AlphaC.CertificatePattern.AnyDrep(default), () => BetaC.CertificatePattern.AnyDrep(default),
            () => AlphaC.CertificatePattern.StakeRegistration(k, k), () => BetaC.CertificatePattern.StakeRegistration(k, k),
            () => AlphaC.CertificatePattern.StakeDeregistration(k, k), () => BetaC.CertificatePattern.StakeDeregistration(k, k),
            () => AlphaC.CertificatePattern.StakeDelegation(k, k), () => BetaC.CertificatePattern.StakeDelegation(k, k)];
        foreach (Action build in empty) Assert.ThrowsAny<ArgumentException>(build);
        Assert.Equal("stakeCredential", Assert.ThrowsAny<ArgumentException>(() => BetaC.CertificatePattern.AnyStakeCredential(default)).ParamName);
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

        ReadOnlyMemory<byte> pool = new byte[] { 21, 22 };
        Compare("HasCertificate(StakeDelegation)", () => BetaW.TxPredicate.HasCertificate(BetaC.CertificatePattern.StakeDelegation(addrKeyHash: p, poolKeyhash: pool)),
            () => new BetaW.TxPredicate { Match = new() { Cardano = new() { HasCertificate = new() { StakeDelegation = new() { StakeCredential = new() { AddrKeyHash = UnsafeByteOperations.UnsafeWrap(p) }, PoolKeyhash = UnsafeByteOperations.UnsafeWrap(pool) } } } } });
        Compare("Consumes", () => BetaW.TxPredicate.Consumes(paymentPart: p, policyId: pool),
            () => new BetaW.TxPredicate { Match = new() { Cardano = new() { Consumes = new() { Address = new() { PaymentPart = UnsafeByteOperations.UnsafeWrap(p) }, Asset = new() { PolicyId = UnsafeByteOperations.UnsafeWrap(pool) } } } } });
        Compare("Match", () => BetaQ.UtxoPredicate.Match(paymentPart: p, policyId: pool),
            () => new BetaQ.UtxoPredicate { Match = new() { Cardano = new() { Address = new() { PaymentPart = UnsafeByteOperations.UnsafeWrap(p) }, Asset = new() { PolicyId = UnsafeByteOperations.UnsafeWrap(pool) } } } });
        void Compare(string name, Func<IMessage> built, Func<IMessage> manual)
        {
            for (int i = 0; i < 1000; i++) { _ = built(); _ = manual(); }
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) _ = built();
            long middle = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) _ = manual();
            long end = GC.GetAllocatedBytesForCurrentThread();
            output.WriteLine($"{name} builder={middle - start}, manual={end - middle} bytes for 1000 calls.");
            Assert.Equal(end - middle, middle - start);
        }

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

    /// <summary>Transmits the new transaction match fields through both services and protocol versions.</summary>
    [Fact]
    public async Task BuiltTxPredicatesReachBothServices()
    {
        await using FakeServer server = await FakeServer.StartAsync();
        using UtxorpcClient client = new(server.Url);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        byte[] p = [1, 2], q = [3, 4], pool = [5, 6];
        BetaW.TxPredicate expected = new()
        {
            AllOf = {
                new BetaW.TxPredicate { Match = new() { Cardano = new() { Consumes = new() { Address = new() { PaymentPart = ByteString.CopyFrom(p) } } } } },
                new BetaW.TxPredicate { Match = new() { Cardano = new() { Produces = new() { Asset = new() { PolicyId = ByteString.CopyFrom(q) } } } } },
                new BetaW.TxPredicate { Match = new() { Cardano = new() { MintsAsset = new() { PolicyId = ByteString.CopyFrom(q) } } } },
                new BetaW.TxPredicate { Match = new() { Cardano = new() { HasCertificate = new() { AnyPoolKeyhash = ByteString.CopyFrom(pool) } } } } }
        };
        await foreach (V1Beta.WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(BetaW.TxPredicate.AllOf(BetaW.TxPredicate.Consumes(paymentPart: p), BetaW.TxPredicate.Produces(policyId: q), BetaW.TxPredicate.MintsAsset(policyId: q), BetaW.TxPredicate.HasCertificate(BetaC.CertificatePattern.AnyPoolKeyhash(pool))), null, cancellationToken: cancellation.Token)) break;
        await foreach (V1Alpha.WatchTxEvent _ in client.V1Alpha.Watch.WatchTxAsync(AlphaW.TxPredicate.AllOf(AlphaW.TxPredicate.Consumes(paymentPart: p), AlphaW.TxPredicate.Produces(policyId: q), AlphaW.TxPredicate.MintsAsset(policyId: q), AlphaW.TxPredicate.HasCertificate(AlphaC.CertificatePattern.AnyPoolKeyhash(pool))), null, cancellationToken: cancellation.Token)) break;
        await foreach (BetaS.WatchMempoolResponse _ in client.V1Beta.Submit.WatchMempoolAsync(BetaS.TxPredicate.AllOf(BetaS.TxPredicate.Consumes(paymentPart: p), BetaS.TxPredicate.Produces(policyId: q), BetaS.TxPredicate.MintsAsset(policyId: q), BetaS.TxPredicate.HasCertificate(BetaC.CertificatePattern.AnyPoolKeyhash(pool))), cancellationToken: cancellation.Token)) break;
        await foreach (AlphaS.WatchMempoolResponse _ in client.V1Alpha.Submit.WatchMempoolAsync(AlphaS.TxPredicate.AllOf(AlphaS.TxPredicate.Consumes(paymentPart: p), AlphaS.TxPredicate.Produces(policyId: q), AlphaS.TxPredicate.MintsAsset(policyId: q), AlphaS.TxPredicate.HasCertificate(AlphaC.CertificatePattern.AnyPoolKeyhash(pool))), cancellationToken: cancellation.Token)) break;
        Assert.Equal(4, server.State.Requests.Count);
        foreach (IMessage request in server.State.Requests)
        {
            IMessage actual = (IMessage)request.Descriptor.FindFieldByName("predicate").Accessor.GetValue(request);
            Assert.Equal(actual.Descriptor.Parser.ParseFrom(expected.ToByteString()), actual);
            Assert.Equal(expected.ToByteString(), actual.ToByteString());
        }
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
