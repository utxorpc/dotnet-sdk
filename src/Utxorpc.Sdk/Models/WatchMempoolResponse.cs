namespace Utxorpc.Sdk.Models;

public record WatchMempoolResponse(
    TxInMempool? Tx
);