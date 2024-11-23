using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;

var headers = new Dictionary<string, string>
{
    { "dmtr-api-key", "your-api-key" },
};

var client = new SyncServiceClient(
    url: "http://localhost:50051",
    headers
);

await foreach (NextResponse? response in client.FollowTipAsync(
    new BlockRef
    (
        "e59489fecba33c244e1e28788ed596f2f2ac3336dd9557271f51dfbd5691be4b",
        65467722
    )))
{
    Console.WriteLine("___________________");
    switch (response.Action)
    {
        case NextResponseAction.Apply:
            Block? applyBlock = response.AppliedBlock;
            if (applyBlock is not null)
            {
                Console.WriteLine($"Apply Block: {applyBlock.Hash} Slot: {applyBlock.Slot}");
                Console.WriteLine($"Cbor: {Convert.ToHexString(applyBlock.NativeBytes ?? [])}");
            }
            break;
        case NextResponseAction.Undo:
            Block? undoBlock = response.UndoneBlock;
            if (undoBlock is not null)
            {
                Console.WriteLine($"Undo Block: {undoBlock.Hash} Slot: {undoBlock.Slot}");
            }
            break;
        case NextResponseAction.Reset:
            BlockRef? resetRef = response.ResetRef;
            if (resetRef is not null)
            {
                Console.WriteLine($"Reset to Block: {resetRef.Hash} Slot: {resetRef.Index}");
            }
            break;
    }
    Console.WriteLine("___________________");
}