namespace Utxorpc.Sdk.Models;

public record WatchTxResponse(
    byte[]? Raw,
    object? ParsedState = null  // Will hold chain-specific data (e.g., Cardano.Tx)
);