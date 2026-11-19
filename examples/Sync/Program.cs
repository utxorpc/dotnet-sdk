using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk;
using Utxorpc.Sdk.V1Beta;
using Utxorpc.V1Beta.Sync;
using Beta = Utxorpc.V1Beta;

// Reads the tip, fetches its block, then follows the chain from there.
// Usage: dotnet run --project examples/Sync -- [events to read; 0 reads until Ctrl+C]
string url = Environment.GetEnvironmentVariable("UTXORPC_URL") ?? "http://localhost:50051";
string? apiKey = Environment.GetEnvironmentVariable("DMTR_API_KEY");
int limit = args.Length > 0 ? int.Parse(args[0]) : 5;
using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
CancellationToken ct = cancellation.Token;

Dictionary<string, string>? headers = apiKey is null ? null : new() { ["dmtr-api-key"] = apiKey };
using UtxorpcClient u5c = new(url, headers);
try
{
    Beta.Sync.BlockRef tip = (await u5c.V1Beta.Sync.ReadTipAsync(ct)).Tip;
    Console.WriteLine($"Tip: slot {tip.Slot}, hash {Convert.ToHexString(tip.Hash.Span)}");
    BlockPoint point = new(tip.Slot, tip.Hash.Memory);

    // The mask asks for the block's bytes without its parsed form.
    Beta.Sync.FetchBlockResponse fetched = await u5c.V1Beta.Sync.FetchBlockAsync(point, FieldMask.FromFieldNumbers<AnyChainBlock>(AnyChainBlock.NativeBytesFieldNumber), ct);
    Console.WriteLine($"Block bytes: {fetched.Block[0].NativeBytes.Length}");

    int events = 0;
    await foreach (FollowTipEvent e in u5c.V1Beta.Sync.FollowTipAsync(point, cancellationToken: ct))
    {
        Console.WriteLine(e switch
        {
            BlockApplied a => $"Apply: {a.Block.NativeBytes.Length} bytes",
            BlockUndone u => $"Undo: {u.Block.NativeBytes.Length} bytes",
            Reset r => $"Reset: slot {r.Point.Slot}",
            null => throw new UnreachableException()
        });
        if (++events == limit) break;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Stopped.");
}
