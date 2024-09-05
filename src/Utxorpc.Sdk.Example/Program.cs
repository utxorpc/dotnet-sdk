using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;

var headers = new Dictionary<string, string>
{
    { "dmtr-api-key", "dmtr_utxorpc1vc0m93rynmltysttwm7ns9m3n5cklws6" },
};

var client = new SyncServiceClient(
    url: "https://preview.utxorpc-v0.demeter.run",
    headers
);

await foreach (NextResponse? response in client.FollowTipAsync(
    new BlockRef
    (
        "b977e548f3364b114505f3311a10f89e5f5cf47e815765bce6750a5de48e5951",
        58717900
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
