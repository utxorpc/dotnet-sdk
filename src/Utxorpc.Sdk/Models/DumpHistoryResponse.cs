namespace Utxorpc.Sdk.Models;

public record DumpHistoryResponse(
    IReadOnlyList<Block> Blocks,
    BlockRef? NextToken
);