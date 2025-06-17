namespace Utxorpc.Sdk.Models;

public record AnyUtxoData(
    byte[]? NativeBytes,
    TxoRef? TxoRef,
    object? ParsedState = null 
);
