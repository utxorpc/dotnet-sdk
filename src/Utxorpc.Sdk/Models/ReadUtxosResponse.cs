namespace Utxorpc.Sdk.Models;

public record ReadUtxosResponse(
    IReadOnlyList<AnyUtxoData> Items,
    ChainPoint? LedgerTip
);