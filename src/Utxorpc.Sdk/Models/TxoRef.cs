namespace Utxorpc.Sdk.Models;

public record TxoRef(
    byte[] Hash,
    ulong Index
);

