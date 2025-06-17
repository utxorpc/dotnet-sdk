using Utxorpc.Sdk.Models.Enums;

namespace Utxorpc.Sdk.Models;

public record TxInMempool(
    byte[]? Ref,
    byte[]? NativeBytes,
    Stage Stage,
    AnyUtxoData? ParsedState
);