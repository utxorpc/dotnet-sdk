namespace Utxorpc.Sdk.Models;

public record BlockRef(
    string Hash,
    ulong Index
);