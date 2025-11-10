using Utxorpc.Sdk.Models.Enums;

namespace Utxorpc.Sdk.Models;

public record WatchTxResponse(
    WatchTxAction Action,
    byte[]? Raw = null,
    object? ParsedState = null,
    BlockRef? IdleBlockRef = null
);