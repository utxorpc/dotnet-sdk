namespace Utxorpc.Sdk.Models;

public record ChainPoint(
    ulong Slot,
    byte[] Hash,
    ulong Height,
    ulong Timestamp
);