using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
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

// Create one client, with an optional API key.
Dictionary<string, string>? headers = apiKey is null ? null : new() { ["dmtr-api-key"] = apiKey };
using UtxorpcClient u5c = new(url, headers);

try
{
    // Use plain .NET inputs through both versions on the same channel.
    Beta.Sync.ReadTipResponse tip = await u5c.V1Beta.Sync.ReadTipAsync(cancellation.Token);
    Console.WriteLine($"v1beta: {tip.Tip.Slot} {Convert.ToHexString(tip.Tip.Hash.Span)}");
    Alpha.Sync.ReadTipResponse oldTip = await u5c.V1Alpha.Sync.ReadTipAsync(cancellation.Token);
    Console.WriteLine($"v1alpha: {oldTip.Tip.Slot} {Convert.ToHexString(oldTip.Tip.Hash.Span)}");
    Beta.Sync.FetchBlockResponse block = await u5c.V1Beta.Sync.FetchBlockAsync(
        new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), FieldMask.FromString("native_bytes"), cancellation.Token);
    Console.WriteLine($"Block bytes: {block.Block[0].NativeBytes.Length}");
    int events = 0;
    await foreach (FollowTipEvent e in
        u5c.V1Beta.Sync.FollowTipAsync(new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), cancellationToken: cancellation.Token))
    {
        Console.WriteLine(e switch
        {
            BlockApplied a => $"Apply: {a.Block.NativeBytes.Length} bytes",
            BlockUndone u => $"Undo: {u.Block.NativeBytes.Length} bytes",
            Reset r => $"Reset: {r.Point.Slot}",
            null => throw new UnreachableException()
        });
        if (++events == 3)
        {
            break;
        }
    }

    // Generated calls take the Spec requests directly.
    tip = await u5c.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token);
    Console.WriteLine($"Tip: {tip.Tip.Slot} {Convert.ToHexString(tip.Tip.Hash.Span)}");
    oldTip = await u5c.V1Alpha.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token);
    block = await u5c.V1Beta.Sync.FetchBlockAsync(
        new() { Ref = { tip.Tip }, FieldMask = new() { Paths = { "native_bytes" } } },
        cancellationToken: cancellation.Token);
    Console.WriteLine($"Block bytes: {block.Block[0].NativeBytes.Length}");
    events = 0;
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

// These additional examples are compiled but not called; no transaction is submitted by default.
_ = nameof(OtherCallsAsync);
_ = nameof(TestingAsync);

static async Task OtherCallsAsync(string url, string? apiKey, UtxorpcClient client, ReadOnlyMemory<byte> address, ReadOnlyMemory<byte> txHash,
    ReadOnlyMemory<byte> signedTx, ReadOnlyMemory<byte> datumHash, ReadOnlyMemory<byte> paymentKeyHash,
    ReadOnlyMemory<byte> policyId, ReadOnlyMemory<byte> poolId, BlockPoint olderPoint, CancellationToken cancellationToken)
{
    Beta.Query.ReadParamsResponse parameters = await client.V1Beta.Query.ReadParamsAsync(cancellationToken: cancellationToken);
    Console.WriteLine(parameters.LedgerTip.Slot);
    BlockPoint point = new(parameters.LedgerTip.Slot, parameters.LedgerTip.Hash.Memory);
    // Fetch only native block bytes.
    FieldMask bytesOnly = FieldMask.FromFieldNumbers<Beta.Sync.AnyChainBlock>(Beta.Sync.AnyChainBlock.NativeBytesFieldNumber);
    FieldMask headerOnly = FieldMask.FromString<Beta.Sync.AnyChainBlock>("cardano.header");
    await client.V1Beta.Sync.FetchBlockAsync(point, bytesOnly, cancellationToken);
    Beta.Query.ReadUtxosResponse outputs = await client.V1Beta.Query.ReadUtxosAsync([new(txHash, 0)], cancellationToken: cancellationToken);
    Console.WriteLine(outputs.Items.Count);
    await foreach (Beta.Query.AnyUtxoData output in client.V1Beta.Query.SearchUtxosByAddressAsync(address, cancellationToken: cancellationToken))
    {
        Console.WriteLine(output.NativeBytes.Length);
    }
    // Match outputs by payment key hash and asset policy.
    await foreach (Beta.Query.AnyUtxoData output in client.V1Beta.Query.SearchAllUtxosAsync(
        Beta.Query.UtxoPredicate.Match(paymentPart: paymentKeyHash, policyId: policyId), cancellationToken: cancellationToken))
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

    // Configure a client that creates its own channel.
    UtxorpcClientOptions options = new() { Url = url, MaxAttempts = 1, KeepAliveInterval = Timeout.InfiniteTimeSpan };
    if (apiKey is not null) options.Headers["dmtr-api-key"] = apiKey;
    using UtxorpcClient configured = new(options);

    ReadOnlyMemory<byte> tx = signedTx;
    try
    {
        await client.V1Beta.Submit.SubmitTxAsync(tx, cancellationToken);
    }
    catch (UtxorpcException e) when (e.Kind == UtxorpcErrorKind.InvalidTx)
    {
        Console.WriteLine(e.Detail);
    }

    // Give every unary call without its own deadline a timeout, retries included.
    UtxorpcClientOptions timed = new() { Url = url, DefaultTimeout = TimeSpan.FromSeconds(30) };
    using UtxorpcClient timedClient = new(timed);
    try
    {
        Beta.Sync.ReadTipResponse timedTip = await timedClient.V1Beta.Sync.ReadTipAsync(cancellationToken);
        Console.WriteLine(timedTip.Tip.Slot);
    }
    catch (RpcException exception) when (exception.StatusCode == StatusCode.DeadlineExceeded)
    {
        Console.WriteLine("Deadline exceeded.");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Cancelled.");
    }
    using UtxorpcClient largerMessages = new(url,
        configureChannel: o => o.MaxReceiveMessageSize = 16 * 1024 * 1024);

    UtxorpcClientOptions resuming = new()
    {
        Url = url,
        ResumeOnFailure = true,
        ResumeAttempts = 10,
        TimeProvider = TimeProvider.System
    };
    using UtxorpcClient resumable = new(resuming);

    // Share clients through dependency injection, with an optional name.
    ServiceCollection services = new();
    services.AddUtxorpcClient(o => o.Url = url);
    services.AddUtxorpcClient("preprod", o => o.Url = url);
    services.AddSingleton(new Uri(url));
    services.AddUtxorpcClient("fromServices", (provider, o) => o.Url = provider.GetRequiredService<Uri>().ToString());
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
    await foreach (Beta.Submit.WatchMempoolResponse _ in client.V1Beta.Submit.WatchMempoolAsync(cancellationToken: cancellationToken)) break;
    // Read every retained history page from the snapshot's tip.
    await foreach (Beta.Sync.AnyChainBlock historicalBlock in client.V1Beta.Sync.DumpAllHistoryAsync(
        point, pageSize: 10, fieldMask: bytesOnly, cancellationToken: cancellationToken))
        Console.WriteLine(historicalBlock.NativeBytes.Length);
    // Watch chain transactions, stopping after the first.
    await foreach (WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(cancellationToken: cancellationToken)) break;
    // Match stake delegations to a pool, starting at one of the intersection candidates.
    Beta.Watch.TxPredicate delegations = Beta.Watch.TxPredicate.HasCertificate(
        Beta.Cardano.CertificatePattern.StakeDelegation(poolKeyhash: poolId));
    await foreach (WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(delegations, [point, olderPoint], cancellationToken: cancellationToken)) break;
    // An address and asset in the same output.
    Beta.Watch.TxPredicate touches = Beta.Watch.TxPredicate.Produces(paymentPart: paymentKeyHash, policyId: policyId);
    await foreach (WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(touches, cancellationToken: cancellationToken)) break;
    await foreach (WatchTxEvent e in client.V1Beta.Watch.WatchTxAsync(cancellationToken: cancellationToken))
    {
        Console.WriteLine(e switch
        {
            TxApplied a => $"Apply: {a.Tx.Cardano?.Hash.Length}",
            TxUndone u => $"Undo: {u.Tx.Cardano?.Hash.Length}",
            Idle i => $"Idle: {i.Point.Slot}",
            null => throw new UnreachableException()
        });
        break;
    }
    await foreach (FollowTipEvent _ in client.V1Beta.Sync.FollowTipAsync(cancellationToken: cancellationToken)) break;
}

static async Task TestingAsync()
{
    FakeChannel channel = new(new FakeInvoker(request => request is Beta.Sync.ReadTipRequest
        ? new Beta.Sync.ReadTipResponse { Tip = new() { Slot = 42 } } : null));
    using UtxorpcClient client = new(channel);
    Console.WriteLine((await client.V1Beta.Sync.ReadTipAsync()).Tip.Slot); // 42
    try
    {
        await client.V1Beta.Query.ReadParamsAsync();
    }
    catch (UtxorpcException error) when (error.Kind == UtxorpcErrorKind.NotFound)
    {
        Console.WriteLine(error.Detail); // not in the fake
    }
}

sealed class FakeChannel(CallInvoker invoker) : ChannelBase("test")
{
    public override CallInvoker CreateCallInvoker() => invoker;
}
sealed class FakeInvoker(Func<object, object?> answer) : CallInvoker
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Task<TResponse> response = answer(request) is TResponse value ? Task.FromResult(value)
            : Task.FromException<TResponse>(new RpcException(new Status(StatusCode.NotFound, "not in the fake")));
        return new AsyncUnaryCall<TResponse>(response, Task.FromResult(Metadata.Empty), () => Status.DefaultSuccess, () => Metadata.Empty, () => { });
    }
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
}
