using Utxorpc.Sdk.Models.Enums;

namespace Utxorpc.Sdk.Models;

public record WaitForTxResponse(
    byte[]? Ref,
    Stage Stage
);