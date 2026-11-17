using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Utxorpc.Sdk.V1Beta;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test;

/// <summary>Holds the README sample lines that no example program shows. Compiled, never called.</summary>
internal static class ReadmeSamples
{
    internal static async Task GeneratedCallsAsync(string[] args, CancellationToken ct)
    {
        string url = args.Length > 0 ? args[0] : "http://localhost:50051";
        using UtxorpcClient u5c = new(url);
        Beta.Sync.ReadTipResponse response = await u5c.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: ct);
        Alpha.Sync.ReadTipResponse oldTip = await u5c.V1Alpha.Sync.ReadTipAsync(new(), cancellationToken: ct);
        Beta.Sync.FetchBlockResponse block = await u5c.V1Beta.Sync.FetchBlockAsync(
            new() { Ref = { response.Tip }, FieldMask = new() { Paths = { "native_bytes" } } },
            cancellationToken: ct);
        int events = 0;
        await foreach (Beta.Sync.FollowTipResponse update in
            u5c.V1Beta.Sync.FollowTip(new() { Intersect = { response.Tip } }, cancellationToken: ct).ReadAllAsync())
        {
            Console.WriteLine(update.ActionCase);
            if (++events == 3)
            {
                break;
            }
        }
    }

    internal static async Task OtherCallsAsync(string url, string? apiKey, UtxorpcClient client,
        ReadOnlyMemory<byte> signedTx, ReadOnlyMemory<byte> paymentKeyHash, ReadOnlyMemory<byte> policyId,
        ReadOnlyMemory<byte> poolId, BlockPoint point, BlockPoint olderPoint, CancellationToken cancellationToken)
    {
        // Fetch only native block bytes.
        FieldMask bytesOnly = FieldMask.FromFieldNumbers<Beta.Sync.AnyChainBlock>(Beta.Sync.AnyChainBlock.NativeBytesFieldNumber);
        FieldMask headerOnly = FieldMask.FromString<Beta.Sync.AnyChainBlock>("cardano.header");
        await client.V1Beta.Sync.FetchBlockAsync(point, bytesOnly, cancellationToken);
        // Match outputs by payment key hash and asset policy.
        await foreach (Beta.Query.AnyUtxoData output in client.V1Beta.Query.SearchAllUtxosAsync(
            Beta.Query.UtxoPredicate.Match(paymentPart: paymentKeyHash, policyId: policyId), cancellationToken: cancellationToken))
            Console.WriteLine(output.NativeBytes.Length);

        // Disable compression when a browser cannot read the server's Grpc-Encoding header.
        UtxorpcClientOptions browserOptions = new() { Url = url, ConfigureChannel = o => o.CompressionProviders = [] };

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

        // Share named clients through dependency injection.
        ServiceCollection services = new();
        services.AddUtxorpcClient("preprod", o => o.Url = url);
        services.AddSingleton(new Uri(url));
        services.AddUtxorpcClient("fromServices", (provider, o) => o.Url = provider.GetRequiredService<Uri>().ToString());
        using ServiceProvider provider = services.BuildServiceProvider();
        UtxorpcClient preprod = provider.GetRequiredKeyedService<UtxorpcClient>("preprod");

        // Read every retained history page from the snapshot's tip.
        await foreach (Beta.Sync.AnyChainBlock historicalBlock in client.V1Beta.Sync.DumpAllHistoryAsync(
            point, pageSize: 10, fieldMask: bytesOnly, cancellationToken: cancellationToken))
            Console.WriteLine(historicalBlock.NativeBytes.Length);
        // Match stake delegations to a pool, starting at one of the intersection candidates.
        Beta.Watch.TxPredicate delegations = Beta.Watch.TxPredicate.HasCertificate(
            Beta.Cardano.CertificatePattern.StakeDelegation(poolKeyhash: poolId));
        await foreach (WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(delegations, [point, olderPoint], cancellationToken: cancellationToken)) break;
        // An address and asset in the same output.
        Beta.Watch.TxPredicate touches = Beta.Watch.TxPredicate.Produces(paymentPart: paymentKeyHash, policyId: policyId);
        await foreach (WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(touches, cancellationToken: cancellationToken)) break;
    }

    internal static async Task TestingAsync()
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
}
