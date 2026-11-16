namespace Utxorpc.Sdk;

/// <summary>Identifies a block; hash memory is wrapped without copying and must not change until the returned task completes or enumeration ends.</summary>
public readonly record struct BlockPoint(ulong Slot, ReadOnlyMemory<byte> Hash);
