namespace Utxorpc.Sdk;

/// <summary>Identifies a block. Equality compares the slot and the hash bytes.</summary>
/// <param name="Slot">The slot number.</param>
/// <param name="Hash">The block hash bytes.</param>
/// <remarks>Hash memory is wrapped, not copied; it must not change until the task completes or enumeration ends, or while this value is a collection key.</remarks>
public readonly record struct BlockPoint(ulong Slot, ReadOnlyMemory<byte> Hash)
{
    /// <summary>Compares the slot and the hash bytes.</summary>
    public bool Equals(BlockPoint other) => Slot == other.Slot && Hash.Span.SequenceEqual(other.Hash.Span);

    /// <summary>Hashes the slot and the hash bytes.</summary>
    public override int GetHashCode()
    {
        HashCode code = new();
        code.Add(Slot);
        code.AddBytes(Hash.Span);
        return code.ToHashCode();
    }
}
