using Utxorpc.Sdk.Models.Enums;

namespace Utxorpc.Sdk.Models;

public record WatchTxResponse(
    WatchTxAction Action,
    byte[]? Raw,
    object? ParsedState = null
);