using Google.Protobuf;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Beta.Cardano;

namespace Utxorpc.Sdk;

/// <summary>Builds v1beta Cardano certificate patterns from plain .NET inputs.</summary>
public static class V1BetaCardanoExtensions
{
    extension(CertificatePattern)
    {
        /// <include file="Docs.xml" path="docs/member[@name='CertificatePattern.StakeRegistration']/*"/>
        /// <include file="Docs.xml" path="docs/pattern/*"/>
        public static CertificatePattern StakeRegistration(ReadOnlyMemory<byte> addrKeyHash = default, ReadOnlyMemory<byte> scriptHash = default) =>
            new() { StakeRegistration = Credential(addrKeyHash, scriptHash) ?? new() };

        /// <include file="Docs.xml" path="docs/member[@name='CertificatePattern.StakeDeregistration']/*"/>
        /// <include file="Docs.xml" path="docs/pattern/*"/>
        public static CertificatePattern StakeDeregistration(ReadOnlyMemory<byte> addrKeyHash = default, ReadOnlyMemory<byte> scriptHash = default) =>
            new() { StakeDeregistration = Credential(addrKeyHash, scriptHash) ?? new() };

        /// <include file="Docs.xml" path="docs/member[@name='CertificatePattern.StakeDelegation']/*"/>
        /// <include file="Docs.xml" path="docs/pattern/*"/>
        public static CertificatePattern StakeDelegation(ReadOnlyMemory<byte> addrKeyHash = default, ReadOnlyMemory<byte> scriptHash = default, ReadOnlyMemory<byte> poolKeyhash = default) =>
            new() { StakeDelegation = new() { StakeCredential = Credential(addrKeyHash, scriptHash), PoolKeyhash = Predicates.Wrap(poolKeyhash) } };

        /// <include file="Docs.xml" path="docs/member[@name='CertificatePattern.PoolRegistration']/*"/>
        /// <include file="Docs.xml" path="docs/pattern/*"/>
        public static CertificatePattern PoolRegistration(ReadOnlyMemory<byte> poolOperator = default, ReadOnlyMemory<byte> poolKeyhash = default) =>
            new() { PoolRegistration = new() { Operator = Predicates.Wrap(poolOperator), PoolKeyhash = Predicates.Wrap(poolKeyhash) } };

        /// <include file="Docs.xml" path="docs/member[@name='CertificatePattern.PoolRetirement']/*"/>
        /// <include file="Docs.xml" path="docs/pattern/*"/>
        public static CertificatePattern PoolRetirement(ReadOnlyMemory<byte> poolKeyhash = default, ulong epoch = 0) =>
            new() { PoolRetirement = new() { PoolKeyhash = Predicates.Wrap(poolKeyhash), Epoch = epoch } };

        /// <include file="Docs.xml" path="docs/member[@name='CertificatePattern.AnyStakeCredential']/*"/>
        /// <include file="Docs.xml" path="docs/pattern/*"/>
        public static CertificatePattern AnyStakeCredential(ReadOnlyMemory<byte> stakeCredential) =>
            new() { AnyStakeCredential = Predicates.Required(stakeCredential, nameof(stakeCredential)) };

        /// <include file="Docs.xml" path="docs/member[@name='CertificatePattern.AnyPoolKeyhash']/*"/>
        /// <include file="Docs.xml" path="docs/pattern/*"/>
        public static CertificatePattern AnyPoolKeyhash(ReadOnlyMemory<byte> poolKeyhash) =>
            new() { AnyPoolKeyhash = Predicates.Required(poolKeyhash, nameof(poolKeyhash)) };

        /// <include file="Docs.xml" path="docs/member[@name='CertificatePattern.AnyDrep']/*"/>
        /// <include file="Docs.xml" path="docs/pattern/*"/>
        public static CertificatePattern AnyDrep(ReadOnlyMemory<byte> drep) =>
            new() { AnyDrep = Predicates.Required(drep, nameof(drep)) };
    }

    private static StakeCredential? Credential(ReadOnlyMemory<byte> addrKeyHash, ReadOnlyMemory<byte> scriptHash)
    {
        if (addrKeyHash.IsEmpty && scriptHash.IsEmpty) return null;
        if (!addrKeyHash.IsEmpty && !scriptHash.IsEmpty)
            throw new ArgumentException("Give a key hash or a script hash, not both.", nameof(addrKeyHash) + ", " + nameof(scriptHash));
        return addrKeyHash.IsEmpty
            ? new() { ScriptHash = UnsafeByteOperations.UnsafeWrap(scriptHash) }
            : new() { AddrKeyHash = UnsafeByteOperations.UnsafeWrap(addrKeyHash) };
    }
}
