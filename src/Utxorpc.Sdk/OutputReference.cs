namespace Utxorpc.Sdk;

/// <summary>Identifies a transaction output. Equality compares the hash bytes and the index.</summary>
/// <param name="TxHash">The transaction hash bytes.</param>
/// <param name="Index">The output index.</param>
/// <remarks>Hash memory is wrapped, not copied; it must not change until the task completes or enumeration ends, or while this value is a collection key.</remarks>
public readonly record struct OutputReference(ReadOnlyMemory<byte> TxHash, uint Index)
{
    /// <summary>Compares the hash bytes and the index.</summary>
    public bool Equals(OutputReference other) => Index == other.Index && TxHash.Span.SequenceEqual(other.TxHash.Span);

    /// <summary>Hashes the hash bytes and the index.</summary>
    public override int GetHashCode()
    {
        HashCode code = new();
        code.AddBytes(TxHash.Span);
        code.Add(Index);
        return code.ToHashCode();
    }
}
