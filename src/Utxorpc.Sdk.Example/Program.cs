using Utxorpc.Sdk;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

string url = args.Length > 0 ? args[0] : "http://localhost:50051";
string? apiKey = args.Length > 1 ? args[1] : null;
using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

// Connect once, with an optional API key.
Dictionary<string, string>? headers = apiKey is null ? null : new() { ["dmtr-api-key"] = apiKey };
using UtxorpcClient u5c = new(url, headers);

try
{
    // Read the tip through both versions on the same connection.
    Beta.Sync.ReadTipResponse tip = await u5c.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token);
    Console.WriteLine($"v1beta: {tip.Tip.Slot} {Convert.ToHexString(tip.Tip.Hash.Span)}");
    Alpha.Sync.ReadTipResponse oldTip = await u5c.V1Alpha.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token);
    Console.WriteLine($"v1alpha: {oldTip.Tip.Slot} {Convert.ToHexString(oldTip.Tip.Hash.Span)}");

    // Fetch only the tip block's native bytes.
    Beta.Sync.FetchBlockResponse block = await u5c.V1Beta.Sync.FetchBlockAsync(
        new() { Ref = { tip.Tip }, FieldMask = new() { Paths = { "native_bytes" } } },
        cancellationToken: cancellation.Token);
    Console.WriteLine($"Block bytes: {block.Block[0].NativeBytes.Length}");

    // Follow the tip, leaving after three events to close the stream.
    int events = 0;
    await foreach (Beta.Sync.FollowTipResponse response in
        u5c.V1Beta.Sync.FollowTip(new() { Intersect = { tip.Tip } }, cancellationToken: cancellation.Token).ReadAllAsync())
    {
        Console.WriteLine(response.ActionCase);
        if (++events == 3)
        {
            break;
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled.");
}
