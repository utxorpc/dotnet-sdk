namespace Utxorpc.Sdk.Models;

public record SearchUtxosResponse(
    IReadOnlyList<AnyUtxoData> Items,
    ChainPoint? LedgerTip,
    string? NextToken
);