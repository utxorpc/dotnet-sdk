namespace Utxorpc.Sdk;

/// <summary>Identifies an output; hash memory is wrapped without copying and must not change until the returned task completes.</summary>
public readonly record struct OutputReference(ReadOnlyMemory<byte> TxHash, uint Index);
