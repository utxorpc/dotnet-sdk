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

    // Use plain .NET inputs for the same synchronization calls.
    tip = await u5c.V1Beta.Sync.ReadTipAsync(cancellation.Token);
    Console.WriteLine($"Tip: {tip.Tip.Slot} {Convert.ToHexString(tip.Tip.Hash.Span)}");
    block = await u5c.V1Beta.Sync.FetchBlockAsync(new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), cancellation.Token);
    Console.WriteLine($"Block bytes: {block.Block[0].NativeBytes.Length}");
    events = 0;
    await foreach (Beta.Sync.FollowTipResponse response in
        u5c.V1Beta.Sync.FollowTipAsync(new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), cancellation.Token))
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

// These additional examples are compiled but not called; no transaction is submitted by default.
_ = nameof(OtherCallsAsync);

static async Task OtherCallsAsync(UtxorpcClient client, ReadOnlyMemory<byte> address, ReadOnlyMemory<byte> txHash,
    ReadOnlyMemory<byte> signedTx, CancellationToken cancellationToken)
{
    Beta.Query.ReadParamsResponse parameters = await client.V1Beta.Query.ReadParamsAsync(cancellationToken);
    Console.WriteLine(parameters.LedgerTip.Slot);
    Beta.Query.ReadUtxosResponse outputs = await client.V1Beta.Query.ReadUtxosAsync([new(txHash, 0)], cancellationToken);
    Console.WriteLine(outputs.Items.Count);
    await foreach (Beta.Query.AnyUtxoData output in client.V1Beta.Query.SearchUtxosByAddressAsync(address, cancellationToken: cancellationToken))
    {
        Console.WriteLine(output.NativeBytes.Length);
    }
    Beta.Query.ReadTxResponse transaction = await client.V1Beta.Query.ReadTxAsync(txHash, cancellationToken);
    Console.WriteLine(transaction.Tx.NativeBytes.Length);
    Beta.Submit.SubmitTxResponse submitted = await client.V1Beta.Submit.SubmitTxAsync(signedTx, cancellationToken);
    await foreach (Beta.Submit.WaitForTxResponse response in client.V1Beta.Submit.WaitForTxAsync(submitted.Ref.Memory, cancellationToken))
    {
        Console.WriteLine(response.Stage);
        if (response.Stage == Beta.Submit.Stage.Confirmed) break;
    }
    Beta.Query.ReadStateResponse state = await client.V1Beta.Query.ReadStateAsync(new Utxorpc.V1Beta.Cardano.StateQuery { }, cancellationToken);
    Console.WriteLine(state.LedgerTip.Slot);
}
