namespace Utxorpc.Sdk.Models;

public record BlockRef(
    string Hash,
    ulong Slot,
    ulong? Height = null,
    ulong? Timestamp = null
);