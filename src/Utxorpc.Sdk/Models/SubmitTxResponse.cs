namespace Utxorpc.Sdk.Models;

public record SubmitTxResponse(
    List<byte[]> Refs
);