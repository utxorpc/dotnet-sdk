namespace Utxorpc.Sdk.Models;

public record ReadParamsResponse(
    AnyChainParams? Values,
    ChainPoint? LedgerTip
);