using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Utxorpc.Sdk;
using Utxorpc.Sdk.V1Beta;
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
    block = await u5c.V1Beta.Sync.FetchBlockAsync(new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), cancellationToken: cancellation.Token);
    Console.WriteLine($"Block bytes: {block.Block[0].NativeBytes.Length}");
    events = 0;
    await foreach (FollowTipEvent e in
        u5c.V1Beta.Sync.FollowTipAsync(new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), cancellationToken: cancellation.Token))
    {
        Console.WriteLine(e switch
        {
            BlockApplied a => $"Apply: {a.Block.NativeBytes.Length} bytes",
            BlockUndone u => $"Undo: {u.Block.NativeBytes.Length} bytes",
            Reset r => $"Reset: {r.Point.Slot}"
        });
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

static async Task OtherCallsAsync(string url, string? apiKey, UtxorpcClient client, ReadOnlyMemory<byte> address, ReadOnlyMemory<byte> txHash,
    ReadOnlyMemory<byte> signedTx, ReadOnlyMemory<byte> datumHash, ReadOnlyMemory<byte> paymentKeyHash,
    ReadOnlyMemory<byte> policyId, CancellationToken cancellationToken)
{
    Beta.Query.ReadParamsResponse parameters = await client.V1Beta.Query.ReadParamsAsync(cancellationToken: cancellationToken);
    Console.WriteLine(parameters.LedgerTip.Slot);
    BlockPoint point = new(parameters.LedgerTip.Slot, parameters.LedgerTip.Hash.Memory);
    // Fetch only native block bytes.
    await client.V1Beta.Sync.FetchBlockAsync(point, FieldMask.FromString("native_bytes"), cancellationToken);
    Beta.Query.ReadUtxosResponse outputs = await client.V1Beta.Query.ReadUtxosAsync([new(txHash, 0)], cancellationToken: cancellationToken);
    Console.WriteLine(outputs.Items.Count);
    await foreach (Beta.Query.AnyUtxoData output in client.V1Beta.Query.SearchUtxosByAddressAsync(address, cancellationToken: cancellationToken))
    {
        Console.WriteLine(output.NativeBytes.Length);
    }
    // Match outputs by payment key hash and asset policy.
    await foreach (Beta.Query.AnyUtxoData output in client.V1Beta.Query.SearchAllUtxosAsync(
        Beta.Query.UtxoPredicate.AllOf(Beta.Query.UtxoPredicate.Address(paymentPart: paymentKeyHash),
            Beta.Query.UtxoPredicate.Asset(policyId: policyId)), cancellationToken: cancellationToken))
        Console.WriteLine(output.NativeBytes.Length);
    Beta.Query.ReadTxResponse transaction = await client.V1Beta.Query.ReadTxAsync(txHash, cancellationToken: cancellationToken);
    Console.WriteLine(transaction.Tx.NativeBytes.Length);
    Beta.Submit.SubmitTxResponse submitted = await client.V1Beta.Submit.SubmitTxAsync(signedTx, cancellationToken);
    await foreach (Beta.Submit.WaitForTxResponse response in client.V1Beta.Submit.WaitForTxAsync(submitted.Ref.Memory, cancellationToken))
    {
        Console.WriteLine(response.Stage);
        if (response.Stage == Beta.Submit.Stage.Confirmed) break;
    }
    Beta.Query.ReadStateResponse state = await client.V1Beta.Query.ReadStateAsync(new Utxorpc.V1Beta.Cardano.StateQuery { }, cancellationToken: cancellationToken);
    Console.WriteLine(state.LedgerTip.Slot);

    // Configure a client that creates its own connection.
    UtxorpcClientOptions options = new() { Url = url, MaxAttempts = 3, KeepAliveInterval = TimeSpan.FromSeconds(60) };
    if (apiKey is not null) options.Headers["dmtr-api-key"] = apiKey;
    using UtxorpcClient configured = new(options);

    // Share clients through dependency injection, with an optional name.
    ServiceCollection services = new();
    services.AddUtxorpcClient(o => o.Url = url);
    services.AddUtxorpcClient("preprod", o => o.Url = url);
    using ServiceProvider provider = services.BuildServiceProvider();
    UtxorpcClient shared = provider.GetRequiredService<UtxorpcClient>();
    UtxorpcClient preprod = provider.GetRequiredKeyedService<UtxorpcClient>("preprod");

    // Read the chain's genesis configuration.
    await client.V1Beta.Query.ReadGenesisAsync(cancellationToken: cancellationToken);
    // Read the chain's era summaries.
    await client.V1Beta.Query.ReadEraSummaryAsync(cancellationToken: cancellationToken);
    // Read data by datum hash.
    await client.V1Beta.Query.ReadDataAsync([datumHash], cancellationToken: cancellationToken);
    // Evaluate a transaction without submitting it.
    await client.V1Beta.Submit.EvalTxAsync(signedTx, cancellationToken);
    // Read the current mempool.
    await client.V1Beta.Submit.ReadMempoolAsync(cancellationToken);
    // Watch mempool changes, stopping after the first.
    await foreach (Beta.Submit.WatchMempoolResponse _ in client.V1Beta.Submit.WatchMempoolAsync(null, cancellationToken: cancellationToken)) break;
    // Read every retained history page from the snapshot's tip.
    await foreach (Beta.Sync.AnyChainBlock historicalBlock in client.V1Beta.Sync.DumpAllHistoryAsync(
        point, pageSize: 10, cancellationToken: cancellationToken))
        Console.WriteLine(historicalBlock.NativeBytes.Length);
    // Watch chain transactions, stopping after the first.
    await foreach (WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: cancellationToken)) break;
}
