using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Utxorpc.Sdk.Test.Fakes;
using Xunit.Abstractions;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test;

/// <summary>Starts a fresh server and timeout for each client test case.</summary>
public abstract class ClientTestBase : IAsyncLifetime
{
    private protected FakeServer server = null!;
    private protected readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));

    /// <summary>Starts a fresh HTTP/2 server for each case.</summary>
    public async Task InitializeAsync() => server = await FakeServer.StartAsync();

    /// <summary>Warms the connection before a short timeout and removes only its setup call.</summary>
    private protected async Task WarmConnectionAsync(UtxorpcClient client)
    {
        await client.V1Beta.Sync.ReadTipAsync(new(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: cancellation.Token);
        server.State.Calls.Clear();
    }

    /// <summary>Releases the server and the test timeout.</summary>
    public async Task DisposeAsync()
    {
        cancellation.Dispose();
        await server.DisposeAsync();
    }

}

/// <summary>Checks channel defaults, retry boundaries, transport ownership and client registration.</summary>
[Trait("Category", "Unit")]
public sealed class ClientOptionsTests(ITestOutputHelper output) : ClientTestBase
{
    /// <summary>Retries an unavailable read and observes both requests on the server.</summary>
    [Fact]
    public async Task UnavailableCallIsRetried()
    {
        server.State.FailWith = StatusCode.Unavailable;
        server.State.FailFirstCalls = 1;
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url });
        Assert.Equal(42UL, (await client.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 2);
    }

    /// <summary>Never resubmits a transaction when its answer is unavailable.</summary>
    [Theory]
    [InlineData("v1alpha")]
    [InlineData("v1beta")]
    public async Task SubmitTxIsNeverRetried(string version)
    {
        server.State.FailWith = StatusCode.Unavailable;
        server.State.FailFirstCalls = 1;
        using UtxorpcClient client = new(server.Url);
        UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(async () =>
        {
            if (version == "v1alpha") await client.V1Alpha.Submit.SubmitTxAsync(new byte[] { 1 }, cancellation.Token);
            else await client.V1Beta.Submit.SubmitTxAsync(new byte[] { 1 }, cancellation.Token);
        });
        Assert.Equal(UtxorpcErrorKind.Unavailable, error.Kind);
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        AssertCalls($"/utxorpc.{version}.submit.SubmitService/SubmitTx", 1);
    }

    /// <summary>Disables retry with one total attempt.</summary>
    [Fact]
    public async Task MaxAttemptsOfOneTurnsRetryOff()
    {
        server.State.FailWith = StatusCode.Unavailable;
        server.State.FailFirstCalls = 1;
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1 });
        UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(() => client.V1Beta.Sync.ReadTipAsync(cancellation.Token));
        Assert.Equal(UtxorpcErrorKind.Unavailable, error.Kind);
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 1);
    }

    /// <summary>Normalizes each expired deadline, including headers and individual stream reads.</summary>
    [Theory]
    [InlineData("unary", "v1alpha", 3)]
    [InlineData("unary", "v1beta", 3)]
    [InlineData("unary", "v1alpha", 1)]
    [InlineData("unary", "v1beta", 1)]
    [InlineData("stream", "v1alpha", 3)]
    [InlineData("stream", "v1beta", 3)]
    [InlineData("stream", "v1alpha", 1)]
    [InlineData("stream", "v1beta", 1)]
    [InlineData("blocking", "v1beta", 1)]
    [InlineData("read", "v1beta", 3)]
    [InlineData("headers", "v1beta", 3)]
    [InlineData("headers", "v1beta", 1)]
    [InlineData("stream-headers", "v1beta", 3)]
    [InlineData("stream-headers", "v1beta", 1)]
    public async Task ExpiredDeadlineThrowsDeadlineExceeded(string call, string version, int maxAttempts)
    {
        server.State.HoldSecondPage = true;
        server.State.StreamMessages = 0;
        using UtxorpcClient client = maxAttempts == 1
            ? new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1 }) : new(server.Url);
        await WarmConnectionAsync(client);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(300);
        RpcException error = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            if (call == "blocking")
                await Task.Run(() => client.V1Beta.Query.SearchUtxos(new() { StartToken = "p2" }, deadline: deadline, cancellationToken: cancellation.Token), cancellation.Token);
            else if (call == "unary" || call == "headers")
            {
                if (version == "v1alpha")
                    await client.V1Alpha.Query.SearchUtxosAsync(new() { StartToken = "p2" }, deadline: deadline, cancellationToken: cancellation.Token);
                else
                {
                    using AsyncUnaryCall<Beta.Query.SearchUtxosResponse> unary = client.V1Beta.Query.SearchUtxosAsync(new() { StartToken = "p2" }, deadline: deadline, cancellationToken: cancellation.Token);
                    if (call == "headers") Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(() => unary.ResponseHeadersAsync)).StatusCode);
                    await unary.ResponseAsync;
                }
            }
            else if (version == "v1alpha")
            {
                await foreach (Alpha.Sync.FollowTipResponse _ in client.V1Alpha.Sync.FollowTip(new(), deadline: deadline, cancellationToken: cancellation.Token).ReadAllAsync(cancellation.Token)) { }
            }
            else
            {
                using AsyncServerStreamingCall<Beta.Sync.FollowTipResponse> stream = client.V1Beta.Sync.FollowTip(new(), deadline: deadline, cancellationToken: cancellation.Token);
                if (call == "stream-headers") Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(() => stream.ResponseHeadersAsync)).StatusCode);
                if (call == "read" || call == "stream-headers") while (await stream.ResponseStream.MoveNext(cancellation.Token)) { }
                else await foreach (Beta.Sync.FollowTipResponse _ in stream.ReadAllAsync(cancellation.Token)) { }
            }
        });
        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
        Assert.False(cancellation.IsCancellationRequested);
        bool streaming = call is "stream" or "read" or "stream-headers";
        if (streaming) await server.State.WaitForStreamEndAsync(version);
        AssertCalls(streaming ? $"/utxorpc.{version}.sync.SyncService/FollowTip" : $"/utxorpc.{version}.query.QueryService/SearchUtxos", 1);
    }

    /// <summary>Preserves caller cancellation even when the call or read starts after its deadline.</summary>
    [Theory]
    [InlineData("unary", 3)]
    [InlineData("unary", 1)]
    [InlineData("stream", 3)]
    [InlineData("unary-precancelled", 1)]
    [InlineData("read-precancelled", 3)]
    public async Task CancelledTokenBeforeTheDeadlineThrowsOperationCanceled(string call, int maxAttempts)
    {
        server.State.HoldSecondPage = true;
        server.State.StreamMessages = 0;
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = maxAttempts });
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        bool precancelled = call.EndsWith("precancelled", StringComparison.Ordinal);
        if (precancelled) cancel.Cancel();
        else cancel.CancelAfter(TimeSpan.FromMilliseconds(100));
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(precancelled ? -100 : 30000);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (call == "read-precancelled")
            {
                using AsyncServerStreamingCall<Beta.Sync.FollowTipResponse> stream = client.V1Beta.Sync.FollowTip(new(), deadline: deadline);
                await stream.ResponseStream.MoveNext(cancel.Token);
            }
            else if (call == "stream")
            {
                await foreach (Beta.Sync.FollowTipResponse _ in client.V1Beta.Sync.FollowTip(new(), deadline: deadline, cancellationToken: cancel.Token).ReadAllAsync(cancel.Token)) { }
            }
            else await client.V1Beta.Query.SearchUtxosAsync(new() { StartToken = "p2" }, deadline: deadline, cancellationToken: cancel.Token);
        });
        Assert.False(cancellation.IsCancellationRequested);
    }

    /// <summary>Keeps an observed read cancellation when headers are read after the deadline.</summary>
    [Theory]
    [InlineData("v1alpha")]
    [InlineData("v1beta")]
    public async Task CancelledTokenBeforeTheDeadlineRemainsCancelledForLaterHeaders(string version)
    {
        server.State.StreamMessages = 0;
        using UtxorpcClient client = new(server.Url);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(600);
        if (version == "v1alpha")
        {
            using AsyncServerStreamingCall<Alpha.Sync.FollowTipResponse> stream = client.V1Alpha.Sync.FollowTip(new(), deadline: deadline);
            await Check(stream);
        }
        else
        {
            using AsyncServerStreamingCall<Beta.Sync.FollowTipResponse> stream = client.V1Beta.Sync.FollowTip(new(), deadline: deadline);
            await Check(stream);
        }

        async Task Check<T>(AsyncServerStreamingCall<T> stream)
        {
            using CancellationTokenSource read = new(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ResponseStream.MoveNext(read.Token));
            Assert.True(DateTime.UtcNow < deadline);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ResponseHeadersAsync);
            await Task.Delay(TimeSpan.FromMilliseconds(650), cancellation.Token);
            Assert.Equal(StatusCode.Cancelled, stream.GetStatus().StatusCode);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ResponseHeadersAsync);
        }
    }

    /// <summary>Allows the wire timeout's millisecond rounding without changing early cancellation.</summary>
    [Fact]
    public async Task EarlyServerDeadlineIsDeadlineExceeded()
    {
        AllocationTests.StubChannel channel = new();
        channel.Invoker.Cancelled = true;
        using UtxorpcClient client = new(channel);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(async () => await client.V1Beta.Sync.ReadTipAsync(new(), deadline: DateTime.UtcNow.AddMilliseconds(0.9)))).StatusCode);
            using (AsyncServerStreamingCall<Beta.Sync.FollowTipResponse> stream = client.V1Beta.Sync.FollowTip(new(), deadline: DateTime.UtcNow.AddMilliseconds(0.9)))
                Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(() => stream.ResponseStream.MoveNext(default))).StatusCode);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.V1Beta.Sync.ReadTipAsync(new(), deadline: DateTime.UtcNow.AddSeconds(30)));
            using (AsyncServerStreamingCall<Beta.Sync.FollowTipResponse> stream = client.V1Beta.Sync.FollowTip(new(), deadline: DateTime.UtcNow.AddSeconds(30)))
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ResponseStream.MoveNext(default));
            Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(async () => await client.V1Beta.Sync.ReadTipAsync(new(), deadline: DateTime.MinValue))).StatusCode);
            using (AsyncServerStreamingCall<Beta.Sync.FollowTipResponse> stream = client.V1Beta.Sync.FollowTip(new(), deadline: DateTime.MinValue))
                Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(() => stream.ResponseStream.MoveNext(default))).StatusCode);
        }
    }

    /// <summary>Applies the default to each unary call, but not to explicit deadlines or streams.</summary>
    [Fact]
    public async Task DefaultTimeoutCoversUnaryCallsOnly()
    {
        server.State.HoldSecondPage = true;
        server.State.StreamMessages = 0;
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1, DefaultTimeout = TimeSpan.FromMilliseconds(200) });
        await WarmConnectionAsync(client);
        int pages = 0;
        UtxorpcException paged = await Assert.ThrowsAsync<UtxorpcException>(async () =>
        {
            await foreach (Beta.Query.AnyUtxoData _ in client.V1Beta.Query.SearchAllUtxosAsync(new(), cancellationToken: cancellation.Token)) pages++;
        });
        Assert.Equal(UtxorpcErrorKind.Timeout, paged.Kind);
        Assert.Equal(1, pages);
        Assert.Equal(StatusCode.DeadlineExceeded, paged.StatusCode);
        Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(async () => await client.V1Beta.Query.SearchUtxosAsync(new() { StartToken = "p2" }, cancellationToken: cancellation.Token))).StatusCode);
        Stopwatch clock = Stopwatch.StartNew();
        Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(async () => await client.V1Beta.Query.SearchUtxosAsync(new() { StartToken = "p2" }, deadline: DateTime.UtcNow.AddMilliseconds(600), cancellationToken: cancellation.Token))).StatusCode);
        Assert.True(clock.ElapsedMilliseconds >= 550);
        output.WriteLine($"Explicit deadline with a 200 ms default: {clock.ElapsedMilliseconds} ms.");
        using CancellationTokenSource first = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        first.CancelAfter(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.V1Beta.Query.SearchUtxosAsync(new() { StartToken = "p2" }, cancellationToken: first.Token));
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        stop.CancelAfter(TimeSpan.FromMilliseconds(900));
        clock.Restart();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (V1Beta.FollowTipEvent _ in client.V1Beta.Sync.FollowTipAsync(cancellationToken: stop.Token)) { }
        });
        Assert.True(clock.ElapsedMilliseconds >= 850);
        output.WriteLine($"Stream with a 200 ms default: {clock.ElapsedMilliseconds} ms.");
        Assert.Equal(42UL, (await client.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        Assert.False(cancellation.IsCancellationRequested);
    }

    /// <summary>Rejects null generated requests synchronously before sending a call.</summary>
    [Fact]
    public void NullRequestThrowsOnTheCallersSide()
    {
        using UtxorpcClient client = new(server.Url);
        // UTXORPC001 rejects literal null at compile time; this covers nulls reaching the call at run time.
        Beta.Sync.ReadTipRequest tipRequest = null!;
        Beta.Query.ReadParamsRequest paramsRequest = null!;
        Alpha.Sync.ReadTipRequest oldTipRequest = null!;
        Beta.Sync.FollowTipRequest followRequest = null!;
        Assert.Equal("request", Assert.Throws<ArgumentNullException>(() => client.V1Beta.Sync.ReadTipAsync(tipRequest)).ParamName);
        Assert.Throws<ArgumentNullException>(() => client.V1Beta.Query.ReadParamsAsync(paramsRequest));
        Assert.Throws<ArgumentNullException>(() => client.V1Alpha.Sync.ReadTip(oldTipRequest));
        Assert.Throws<ArgumentNullException>(() => client.V1Beta.Sync.FollowTip(followRequest));
        Assert.Empty(server.State.Calls);
    }

    /// <summary>Configures both ordinary and named clients from the same service provider.</summary>
    [Fact]
    public async Task ServicesConfigureTheClient()
    {
        ServiceCollection services = new();
        services.AddSingleton(server);
        services.AddUtxorpcClient((provider, options) => options.Url = provider.GetRequiredService<FakeServer>().Url);
        services.AddUtxorpcClient("k", (provider, options) => options.Url = provider.GetRequiredService<FakeServer>().Url);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(42UL, (await provider.GetRequiredService<UtxorpcClient>().V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        Assert.Equal(42UL, (await provider.GetRequiredKeyedService<UtxorpcClient>("k").V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddUtxorpcClient(null!));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddUtxorpcClient("a", null!));
    }

    /// <summary>Surfaces a broken stream without replaying any delivered message.</summary>
    [Fact]
    public async Task StreamIsNotRetriedAfterItsFirstMessage()
    {
        server.State.FailAfterFirstMessage = true;
        using UtxorpcClient client = new(server.Url);
        List<ulong> slots = new();
        UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(async () =>
        {
            await foreach (V1Beta.FollowTipEvent response in client.V1Beta.Sync.FollowTipAsync(null, cancellationToken: cancellation.Token))
                slots.Add(response.Response.Reset.Slot);
        });
        Assert.Equal(UtxorpcErrorKind.Unavailable, error.Kind);
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.Equal(new ulong[] { 1 }, slots);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/FollowTip", 1);
        Assert.True(server.State.StreamEnded.Task.IsCompletedSuccessfully);
    }

    /// <summary>Cancels an uncommitted default stream without changing its exception contract.</summary>
    [Theory]
    [InlineData("short")]
    [InlineData("raw")]
    [InlineData("events")]
    public async Task SilentStreamCancellationThrowsOperationCanceled(string path)
    {
        server.State.StreamMessages = 0;
        using UtxorpcClient client = new(server.Url);
        await WarmConnectionAsync(client);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));
        byte[] hash = new byte[32];
        int messages = 0;
        Exception error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (path == "short")
            {
                await foreach (Beta.Submit.WaitForTxResponse _ in client.V1Beta.Submit.WaitForTxAsync(hash, cancel.Token)) messages++;
            }
            else if (path == "events")
            {
                await foreach (V1Beta.FollowTipEvent _ in client.V1Beta.Sync.FollowTipAsync(null, cancellationToken: cancel.Token)) messages++;
            }
            else
            {
                using AsyncServerStreamingCall<Beta.Submit.WaitForTxResponse> call = client.V1Beta.Submit.WaitForTx(
                    new() { Ref = { ByteString.CopyFrom(hash) } }, cancellationToken: cancel.Token);
                while (await call.ResponseStream.MoveNext(cancel.Token)) messages++;
            }
        });
        Assert.IsNotType<RpcException>(error);
        Assert.Equal(0, messages);
        await server.State.WaitForStreamEndAsync("v1beta");
        AssertCalls(path == "events" ? "/utxorpc.v1beta.sync.SyncService/FollowTip" : "/utxorpc.v1beta.submit.SubmitService/WaitForTx", 1);
    }

    /// <summary>Surfaces an unavailable stream before any response without another attempt.</summary>
    [Fact]
    public async Task StreamIsNotRetriedBeforeItsFirstMessage()
    {
        server.State.FailWith = StatusCode.Unavailable;
        server.State.FailFirstCalls = 1;
        using UtxorpcClient client = new(server.Url);
        UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(async () =>
        {
            await foreach (V1Beta.FollowTipEvent _ in client.V1Beta.Sync.FollowTipAsync(null, cancellationToken: cancellation.Token)) { }
        });
        Assert.Equal(UtxorpcErrorKind.Unavailable, error.Kind);
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/FollowTip", 1);
    }

    /// <summary>Exposes the same defaults to both hooks and permits retry and pings to be disabled.</summary>
    [Fact]
    public async Task ConfigureChannelSeesTheDefaults()
    {
        int configured = 0;
        void CheckDefaults(GrpcChannelOptions options)
        {
            configured++;
            Assert.True(options.ThrowOperationCanceledOnCancellation);
            Assert.False(options.DisposeHttpClient);
            SocketsHttpHandler handler = Assert.IsType<SocketsHttpHandler>(options.HttpHandler);
            Assert.True(handler.EnableMultipleHttp2Connections);
            Assert.Equal(TimeSpan.FromSeconds(60), handler.KeepAlivePingDelay);
            Assert.Equal(TimeSpan.FromSeconds(30), handler.KeepAlivePingTimeout);
            Assert.Equal(HttpKeepAlivePingPolicy.WithActiveRequests, handler.KeepAlivePingPolicy);
            Assert.Equal(TimeSpan.FromSeconds(15), handler.ConnectTimeout);
            Assert.Null(options.HttpClient);
            ServiceConfig config = Assert.IsType<ServiceConfig>(options.ServiceConfig);
            Assert.Equal(2, config.MethodConfigs.Count);
            Assert.Same(MethodName.Default, Assert.Single(config.MethodConfigs[0].Names));
            RetryPolicy retry = Assert.IsType<RetryPolicy>(config.MethodConfigs[0].RetryPolicy);
            Assert.Equal(3, retry.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(1), retry.InitialBackoff);
            Assert.Equal(TimeSpan.FromSeconds(5), retry.MaxBackoff);
            Assert.Equal(1.5, retry.BackoffMultiplier);
            Assert.Equal(StatusCode.Unavailable, Assert.Single(retry.RetryableStatusCodes));
            Assert.Null(config.MethodConfigs[1].RetryPolicy);
            Assert.Equal(10, config.MethodConfigs[1].Names.Count);
            (string? Service, string? Method)[] expected = SpecCoverageTests.GeneratedClients()
                .Select(SpecCoverageTests.Descriptor)
                .SelectMany(service => service.Methods.Where(method => method.IsServerStreaming
                    || method.Name == nameof(Beta.Submit.SubmitService.SubmitServiceBase.SubmitTx)))
                .Select(method => ((string?)method.Service.FullName, (string?)method.Name))
                .OrderBy(name => name.Item1, StringComparer.Ordinal).ThenBy(name => name.Item2, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, config.MethodConfigs[1].Names.Select(name => (name.Service, name.Method))
                .OrderBy(name => name.Service, StringComparer.Ordinal).ThenBy(name => name.Method, StringComparer.Ordinal));
        }
        using UtxorpcClient fromOptions = new(new UtxorpcClientOptions { Url = server.Url, ConfigureChannel = CheckDefaults });
        using UtxorpcClient fromUrl = new(server.Url, configureChannel: CheckDefaults);
        using UtxorpcClient disabled = new(new UtxorpcClientOptions
        {
            Url = server.Url,
            MaxAttempts = 1,
            KeepAliveInterval = Timeout.InfiniteTimeSpan,
            ConfigureChannel = options =>
            {
                configured++;
                Assert.Null(options.ServiceConfig);
                Assert.Equal(Timeout.InfiniteTimeSpan, Assert.IsType<SocketsHttpHandler>(options.HttpHandler).KeepAlivePingDelay);
            }
        });
        foreach (UtxorpcClient client in new[] { fromOptions, fromUrl, disabled })
            Assert.Equal(42UL, (await client.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        Assert.Equal(3, configured);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 3);
    }

    /// <summary>Sends the options headers with the request.</summary>
    [Fact]
    public async Task OptionsConstructorSendsHeaders()
    {
        UtxorpcClientOptions options = new() { Url = server.Url, Headers = { ["X-Test"] = "0", ["x-test"] = "1" } };
        using UtxorpcClient client = new(options);
        Assert.Equal(42UL, (await client.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 1);
        Assert.Equal(new KeyValuePair<string, string>("x-test", "1"), Assert.Single(Assert.Single(server.State.Calls).Headers, header => header.Key == "x-test"));
        Assert.DoesNotContain("1", options.ToString()!);
    }

    /// <summary>Rejects invalid options and registration arguments with their exact exception types.</summary>
    [Fact]
    public async Task InvalidOptionsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new UtxorpcClient((UtxorpcClientOptions)null!));
        ArgumentNullException missing = Assert.Throws<ArgumentNullException>(() => new UtxorpcClient(new UtxorpcClientOptions()));
        Assert.Equal("options", missing.ParamName);
        Assert.Contains("UtxorpcClientOptions.Url is required.", missing.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new UtxorpcClient(new UtxorpcClientOptions { Url = " " }));
        Assert.Throws<ArgumentException>(() => new UtxorpcClient(""));
        Assert.Throws<ArgumentException>(() => new UtxorpcClient(" "));
        foreach (int attempts in new[] { 0, 6 })
            Assert.Equal("options", Assert.Throws<ArgumentOutOfRangeException>(() => new UtxorpcClient(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = attempts })).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentOutOfRangeException>(() => new UtxorpcClient(new UtxorpcClientOptions { Url = server.Url, KeepAliveInterval = TimeSpan.Zero })).ParamName);
        Assert.Throws<ArgumentNullException>(() => UtxorpcServiceCollectionExtensions.AddUtxorpcClient(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddUtxorpcClient(null!));
        Assert.Throws<ArgumentNullException>(() => UtxorpcServiceCollectionExtensions.AddUtxorpcClient(null!, "a", _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddUtxorpcClient(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddUtxorpcClient("a", null!));
        foreach (TimeSpan timeout in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-2), TimeSpan.MaxValue, TimeSpan.FromMilliseconds((double)int.MaxValue + 1) })
            Assert.Equal("options", Assert.Throws<ArgumentOutOfRangeException>(() => new UtxorpcClient(new UtxorpcClientOptions { Url = server.Url, DefaultTimeout = timeout })).ParamName);
        foreach (TimeSpan timeout in new[] { Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(int.MaxValue) })
        {
            using UtxorpcClient accepted = new(new UtxorpcClientOptions { Url = server.Url, DefaultTimeout = timeout });
        }
        Assert.Empty(server.State.Calls);
        using UtxorpcClient resolver = new($"dns:/localhost:{new Uri(server.Url).Port}", configureChannel: options => options.Credentials = ChannelCredentials.Insecure);
        Assert.Equal(42UL, (await resolver.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
    }

    /// <summary>Reports a missing scheme at host start unless a hook could provide a resolver for it.</summary>
    [Theory]
    [InlineData("localhost:50051", true)]
    [InlineData("localhost:50051/", true)]
    [InlineData("localhost:50051?value=1:2", true)]
    [InlineData("localhost:50051#fragment", true)]
    [InlineData("dolos:50051", true)]
    [InlineData("127.0.0.1:50061", false)]
    [InlineData("[::1]:50051", false)]
    [InlineData("localhost", false)]
    public async Task UrlWithoutScheme(string url, bool parsesAsCustomScheme)
    {
        UriFormatException direct = Assert.Throws<UriFormatException>(() => new UtxorpcClient(url));
        if (url == "127.0.0.1:50061") Assert.IsType<UriFormatException>(direct.InnerException);
        if (url == "localhost")
            Assert.Equal("The URL is not a valid URI; it needs a scheme, such as http:// or https://.", direct.Message);
        else
        {
            Assert.Contains("missing a scheme", direct.Message, StringComparison.Ordinal);
            Assert.Contains("http://example.com:50051", direct.Message, StringComparison.Ordinal);
            Assert.Contains("https://example.com:50051", direct.Message, StringComparison.Ordinal);
        }
        Assert.Equal(direct.Message, Assert.Throws<UriFormatException>(() => new UtxorpcClient(new UtxorpcClientOptions { Url = url })).Message);

        int hooks = 0;
        Action<GrpcChannelOptions> configure = _ => hooks++;
        UriFormatException hooked = Assert.Throws<UriFormatException>(() => new UtxorpcClient(url, configureChannel: configure));
        Assert.Equal(direct.Message, hooked.Message);
        Assert.Equal(parsesAsCustomScheme ? 1 : 0, hooks);
        if (parsesAsCustomScheme) AssertResolverMiss(hooked);

        foreach (bool withHook in new[] { false, true })
        {
            hooks = 0;
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Services.AddUtxorpcClient("missing-scheme", options =>
            {
                options.Url = url;
                options.ConfigureChannel = withHook ? configure : null;
            });
            using IHost host = builder.Build();
            if (parsesAsCustomScheme && withHook)
            {
                await host.StartAsync(cancellation.Token);
                Assert.Equal(0, hooks);
                UriFormatException error = Assert.Throws<UriFormatException>(() => host.Services.GetRequiredKeyedService<UtxorpcClient>("missing-scheme"));
                Assert.Equal(direct.Message, error.Message);
                AssertResolverMiss(error);
                Assert.Equal(1, hooks);
            }
            else
            {
                OptionsValidationException error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(cancellation.Token));
                Assert.Equal($"Utxorpc client 'missing-scheme': {direct.Message}", error.Message);
                Assert.Equal(0, hooks);
            }
        }

        void AssertResolverMiss(UriFormatException error)
        {
            if (url.Contains('?') || url.Contains('#')) Assert.Null(error.InnerException);
            else Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    /// <summary>Accepts explicit HTTP and HTTPS addresses without needing a resolver.</summary>
    [Theory]
    [InlineData("http://localhost:50051")]
    [InlineData("https://example.com")]
    public void UrlWithHttpScheme(string url)
    {
        using UtxorpcClient direct = new(url);
        using UtxorpcClient configured = new(new UtxorpcClientOptions { Url = url });
    }

    /// <summary>Recognizes gRPC's built-in DNS scheme at host start, before channel credentials are needed.</summary>
    [Fact]
    public async Task BuiltInDnsSchemePassesStartupValidation()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        const string url = "dns:50051";
        builder.Services.AddUtxorpcClient(options => options.Url = url);
        using IHost host = builder.Build();
        await host.StartAsync(cancellation.Token);
        InvalidOperationException grpcError = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress(url));
        Assert.Equal(grpcError.Message, Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<UtxorpcClient>()).Message);
    }

    /// <summary>Names unsupported schemes without a hook at host start and defers hooked schemes to first resolve.</summary>
    [Theory]
    [InlineData("grpc://localhost:50051")]
    [InlineData("static:///localhost:50051")]
    public async Task UnregisteredUrlScheme(string url)
    {
        ArgumentException direct = Assert.Throws<ArgumentException>(() => new UtxorpcClient(url));
        Assert.Contains("has no address resolver", direct.Message, StringComparison.Ordinal);
        Assert.Contains("http:// or https://", direct.Message, StringComparison.Ordinal);
        Assert.Contains("ConfigureChannel", direct.Message, StringComparison.Ordinal);
        Assert.Equal(direct.Message, Assert.Throws<ArgumentException>(() => new UtxorpcClient(new UtxorpcClientOptions { Url = url })).Message);
        InvalidOperationException hooked = Assert.Throws<InvalidOperationException>(() => new UtxorpcClient(url, configureChannel: _ => { }));
        Assert.Equal(direct.Message, hooked.Message);
        Assert.IsType<InvalidOperationException>(hooked.InnerException);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddUtxorpcClient("unsupported", options => options.Url = url);
        using IHost host = builder.Build();
        OptionsValidationException error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(cancellation.Token));
        Assert.Equal($"Utxorpc client 'unsupported': {direct.Message}", error.Message);

        int hooks = 0;
        HostApplicationBuilder hookedBuilder = Host.CreateApplicationBuilder();
        hookedBuilder.Services.AddUtxorpcClient(options =>
        {
            options.Url = url;
            options.ConfigureChannel = _ => hooks++;
        });
        using IHost hookedHost = hookedBuilder.Build();
        await hookedHost.StartAsync(cancellation.Token);
        Assert.Equal(0, hooks);
        Assert.Equal(direct.Message, Assert.Throws<InvalidOperationException>(() => hookedHost.Services.GetRequiredService<UtxorpcClient>()).Message);
        Assert.Equal(1, hooks);
    }

    /// <summary>Does not include sensitive URL parts even in inner exceptions.</summary>
    [Theory]
    [InlineData("http://CANARY_USER:CANARY_secret@host:bad?CANARY_QUERY#CANARY_FRAGMENT", typeof(UriFormatException))]
    [InlineData("dns://CANARY_USER:CANARY_secret@host/service?CANARY_QUERY#CANARY_FRAGMENT", typeof(InvalidOperationException))]
    [InlineData("grpc://CANARY_USER:CANARY_secret@host:50051?CANARY_QUERY#CANARY_FRAGMENT", typeof(InvalidOperationException))]
    [InlineData("dns://CANARY_USER:CANARY_secret@host/service", typeof(InvalidOperationException))]
    [InlineData("dns:///service?CANARY_QUERY", typeof(InvalidOperationException))]
    [InlineData("dns:///service#CANARY_FRAGMENT", typeof(InvalidOperationException))]
    [InlineData("CANARYUSER:CANARYPASS@host:50051", typeof(InvalidOperationException))]
    public void CredentialUrlIsNotEchoed(string url, Type exceptionType)
    {
        Action<GrpcChannelOptions> configure = _ => { };
        Exception direct = Assert.Throws(exceptionType, () => new UtxorpcClient(url, configureChannel: configure));
        Exception configured = Assert.Throws(exceptionType, () => new UtxorpcClient(new UtxorpcClientOptions { Url = url, ConfigureChannel = configure }));
        foreach (Exception error in new[] { direct, configured })
        {
            string diagnostic = error.ToString();
            Assert.DoesNotContain("CANARY_USER", diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CANARYUSER", diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CANARYPASS", diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CANARY_QUERY", diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CANARY_FRAGMENT", diagnostic, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Resolves an explicit custom scheme through each hook, without running the DI hook at host start.</summary>
    [Fact]
    public async Task CustomResolverWorksThroughChannelHook()
    {
        int hooks = 0;
        Uri endpoint = new(server.Url);
        using ServiceProvider resolvers = new ServiceCollection()
            .AddSingleton<ResolverFactory>(new TestResolverFactory(new BalancerAddress(endpoint.Host, endpoint.Port)))
            .BuildServiceProvider();
        void Configure(GrpcChannelOptions options)
        {
            hooks++;
            options.ServiceProvider = resolvers;
            options.Credentials = ChannelCredentials.Insecure;
        }

        const string url = "custom:50051";
        using UtxorpcClient direct = new(url, configureChannel: Configure);
        using UtxorpcClient configured = new(new UtxorpcClientOptions { Url = url, ConfigureChannel = Configure });
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddUtxorpcClient(options => { options.Url = url; options.ConfigureChannel = Configure; });
        using IHost host = builder.Build();
        await host.StartAsync(cancellation.Token);
        Assert.Equal(2, hooks);
        foreach (UtxorpcClient client in new[] { direct, configured, host.Services.GetRequiredService<UtxorpcClient>() })
            Assert.Equal(42UL, (await client.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        Assert.Equal(3, hooks);
    }

    private sealed class TestResolverFactory(BalancerAddress endpoint) : ResolverFactory
    {
        private readonly StaticResolverFactory resolver = new(_ => [endpoint]);

        public override string Name => "custom";
        public override Resolver Create(ResolverOptions options) => resolver.Create(options);
    }

    /// <summary>Rejects invalid resume settings even when resuming is disabled.</summary>
    [Fact]
    public async Task ResumeOptionsAreValidated()
    {
        foreach (int attempts in new[] { 0, -1 })
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new UtxorpcClient(new UtxorpcClientOptions { Url = server.Url, ResumeAttempts = attempts }));
            Assert.Equal("options", error.ParamName);
            Assert.Equal(attempts, error.ActualValue);
            Assert.Contains("ResumeAttempts must be at least 1, or null to keep trying until cancelled.", error.Message, StringComparison.Ordinal);
        }
        ArgumentNullException missing = Assert.Throws<ArgumentNullException>(() => new UtxorpcClient(new UtxorpcClientOptions { Url = server.Url, TimeProvider = null! }));
        Assert.Equal("options", missing.ParamName);
        Assert.Contains("UtxorpcClientOptions.TimeProvider is required.", missing.Message, StringComparison.Ordinal);

        foreach ((string option, Action<UtxorpcClientOptions> invalidate) in new (string, Action<UtxorpcClientOptions>)[]
        {
            ("ResumeAttempts", options => options.ResumeAttempts = 0),
            ("TimeProvider", options => options.TimeProvider = null!)
        })
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Services.AddUtxorpcClient("resuming", options =>
            {
                options.Url = server.Url;
                invalidate(options);
            });
            using IHost host = builder.Build();
            OptionsValidationException error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(cancellation.Token));
            Assert.Contains("Utxorpc client 'resuming':", error.Message, StringComparison.Ordinal);
            Assert.Contains(option, error.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Creates one client lazily and disposes it with the container.</summary>
    [Fact]
    public async Task AddUtxorpcClientRegistersOneSingleton()
    {
        int configured = 0;
        ServiceCollection services = new();
        Assert.Same(services, services.AddUtxorpcClient(options => { configured++; options.Url = server.Url; }));
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(0, configured);
        UtxorpcClient client = provider.GetRequiredService<UtxorpcClient>();
        Assert.Same(client, provider.GetRequiredService<UtxorpcClient>());
        Assert.Equal(1, configured);
        Assert.Equal(42UL, (await client.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        provider.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.V1Beta.Sync.ReadTipAsync(cancellation.Token));
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 1);
    }

    /// <summary>Resolves independent named clients while reusing each named singleton.</summary>
    [Fact]
    public async Task NamedClientsAreSeparateKeyedSingletons()
    {
        ServiceCollection services = new();
        Assert.Same(services, services.AddUtxorpcClient("a", options => options.Url = server.Url));
        services.AddUtxorpcClient("b", options => options.Url = server.Url);
        using ServiceProvider provider = services.BuildServiceProvider();
        UtxorpcClient a = provider.GetRequiredKeyedService<UtxorpcClient>("a");
        UtxorpcClient b = provider.GetRequiredKeyedService<UtxorpcClient>("b");
        Assert.NotSame(a, b);
        Assert.Same(a, provider.GetRequiredKeyedService<UtxorpcClient>("a"));
        Assert.Same(b, provider.GetRequiredKeyedService<UtxorpcClient>("b"));
        Assert.Equal(42UL, (await a.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        Assert.Equal(42UL, (await b.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 2);
        Assert.Equal(2, server.State.Calls.Select(call => call.ConnectionId).Distinct().Count());
    }

    /// <summary>Binds URL, retry settings and the get-only headers dictionary from configuration.</summary>
    [Fact]
    public async Task OptionsBindFromConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Utxorpc:Url"] = server.Url,
            ["Utxorpc:Headers:x-test"] = "1",
            ["Utxorpc:MaxAttempts"] = "2"
        }).Build();
        ServiceCollection services = new();
        services.AddUtxorpcClient(options => configuration.GetSection("Utxorpc").Bind(options));
        using ServiceProvider provider = services.BuildServiceProvider();
        server.State.FailWith = StatusCode.Unavailable;
        server.State.FailFirstCalls = 1;
        Assert.Equal(42UL, (await provider.GetRequiredService<UtxorpcClient>().V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 2);
        Assert.All(server.State.Calls, call => Assert.Contains(new KeyValuePair<string, string>("x-test", "1"), call.Headers));
    }

    /// <summary>Rejects a missing URL at start even if a later registration replaces the client.</summary>
    [Fact]
    public async Task StartFailsNamingTheClientAndTheOption()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddUtxorpcClient("preprod", _ => { });
        builder.Services.AddUtxorpcClient("preprod", options => options.Url = server.Url);
        using IHost host = builder.Build();
        OptionsValidationException error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(cancellation.Token));
        Assert.Equal("Utxorpc client 'preprod': UtxorpcClientOptions.Url is required. (Parameter 'options')", error.Message);
    }

    /// <summary>Reports every invalid registration with its own client name and option failure.</summary>
    [Fact]
    public async Task TwoBadClientsAreBothNamed()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddUtxorpcClient("preprod", _ => { });
        builder.Services.AddUtxorpcClient(options => options.Url = "127.0.0.1:50061");
        using IHost host = builder.Build();
        AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => host.StartAsync(cancellation.Token));
        Assert.Equal(new[]
        {
            "Utxorpc client 'preprod': UtxorpcClientOptions.Url is required. (Parameter 'options')",
            "Utxorpc client: The URL is missing a scheme. Use http://example.com:50051 for plaintext or https://example.com:50051 for TLS."
        }.Order(StringComparer.Ordinal), error.InnerExceptions.Select(exception => exception.Message).Order(StringComparer.Ordinal));
    }

    /// <summary>Names a bad client on first resolve when no host starts the container.</summary>
    [Fact]
    public void WithoutAHostTheFirstResolveNamesTheClient()
    {
        ServiceCollection services = new();
        services.AddUtxorpcClient("preprod", _ => { });
        using ServiceProvider provider = services.BuildServiceProvider();
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<UtxorpcClient>("preprod"));
        Assert.Equal("Utxorpc client 'preprod': UtxorpcClientOptions.Url is required. (Parameter 'options')", error.Message);
    }

    /// <summary>Configures once across host start and resolution, then reads the configured server's tip.</summary>
    [Fact]
    public async Task AValidClientStartsAndResolves()
    {
        int configured = 0;
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(new Uri(server.Url));
        builder.Services.AddUtxorpcClient((provider, options) =>
        {
            configured++;
            options.Url = provider.GetRequiredService<Uri>().ToString();
        });
        using IHost host = builder.Build();
        await host.StartAsync(cancellation.Token);
        Assert.Equal(42UL, (await host.Services.GetRequiredService<UtxorpcClient>().V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
        Assert.Equal(1, configured);
        await host.StopAsync(cancellation.Token);
    }

    /// <summary>Keeps each registration's headers and channel hook isolated, including duplicate and empty keys.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("preprod", "preprod")]
    [InlineData(null, "")]
    public async Task RegistrationsKeepTheirOwnConfiguration(string? firstKey, string? secondKey)
    {
        int hooks = 0;
        ServiceCollection services = new();
        Register(firstKey, options =>
        {
            options.Url = server.Url;
            options.Headers["x-test-marker"] = "first";
            options.ConfigureChannel = _ => hooks++;
        });
        Register(secondKey, options => options.Url = server.Url);
        using ServiceProvider provider = services.BuildServiceProvider();
        UtxorpcClient last = secondKey is null ? provider.GetRequiredService<UtxorpcClient>() : provider.GetRequiredKeyedService<UtxorpcClient>(secondKey);
        await CheckAsync(last, false);
        Assert.Equal(0, hooks);
        UtxorpcClient[] firstGroup = ResolveAll(firstKey);
        UtxorpcClient[] secondGroup = ResolveAll(secondKey);
        Assert.Equal(firstKey == secondKey ? 2 : 1, firstGroup.Length);
        Assert.Equal(firstKey == secondKey ? 2 : 1, secondGroup.Length);
        Assert.NotSame(firstGroup[0], last);
        await CheckAsync(firstGroup[0], true);
        await CheckAsync(secondGroup[^1], false);
        Assert.Same(last, secondGroup[^1]);
        Assert.Equal(1, hooks);

        void Register(string? key, Action<UtxorpcClientOptions> configure)
        {
            if (key is null) services.AddUtxorpcClient(configure);
            else services.AddUtxorpcClient(key, configure);
        }
        UtxorpcClient[] ResolveAll(string? key) => key is null ? provider.GetServices<UtxorpcClient>().ToArray() : provider.GetKeyedServices<UtxorpcClient>(key).ToArray();
        async Task CheckAsync(UtxorpcClient client, bool marked)
        {
            Assert.Equal(42UL, (await client.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
            FakeCall call = Assert.Single(server.State.Calls);
            Assert.Equal(marked ? new[] { new KeyValuePair<string, string>("x-test-marker", "first") } : [], call.Headers.Where(header => header.Key == "x-test-marker"));
            server.State.Calls.Clear();
        }
    }

    /// <summary>Disposes only the SDK transport, even when the hook replaces or wraps it.</summary>
    [Theory]
    [InlineData("options")]
    [InlineData("url")]
    public async Task TransportOwnership(string constructor)
    {
        for (int mode = 0; mode < 4; mode++)
        {
            SocketsHttpHandler sdkHandler = null!;
            TrackingHandler? callerHandler = null;
            HttpClient? callerClient = null;
            void Configure(GrpcChannelOptions options)
            {
                sdkHandler = Assert.IsType<SocketsHttpHandler>(options.HttpHandler);
                if (mode == 0) return;
                callerHandler = new TrackingHandler { InnerHandler = mode == 3 ? sdkHandler : new SocketsHttpHandler() };
                if (mode == 2) options.HttpClient = callerClient = new HttpClient(callerHandler);
                else options.HttpHandler = callerHandler;
            }
            try
            {
                using UtxorpcClient client = constructor == "options"
                    ? new(new UtxorpcClientOptions { Url = server.Url, ConfigureChannel = Configure })
                    : new(server.Url, configureChannel: Configure);
                Assert.Equal(42UL, (await client.V1Beta.Sync.ReadTipAsync(cancellation.Token)).Tip.Slot);
                client.Dispose();
                client.Dispose();
                Assert.Throws<ObjectDisposedException>(() => sdkHandler.ConnectTimeout = TimeSpan.FromSeconds(1));
                if (callerHandler is not null) Assert.Equal(0, callerHandler.DisposeCalls);
            }
            finally
            {
                if (callerClient is not null) callerClient.Dispose();
                else callerHandler?.Dispose();
            }
            if (callerHandler is not null) Assert.Equal(1, callerHandler.DisposeCalls);
        }
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 4);
    }

    private void AssertCalls(string method, int count)
    {
        Assert.Equal(count, server.State.Calls.Count);
        Assert.All(server.State.Calls, call => Assert.Equal(method, call.Method));
    }

    private sealed class TrackingHandler : DelegatingHandler
    {
        public int DisposeCalls { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeCalls++;
            base.Dispose(disposing);
        }
    }
}

/// <summary>Measures short-call parity and the default retry cost without parallel test activity.</summary>
[Trait("Category", "Unit")]
[Collection("Allocation")]
public sealed class ClientAllocationTests(ITestOutputHelper output) : ClientTestBase
{
    private const string ReadTipPath = "/utxorpc.v1beta.sync.SyncService/ReadTip";

    /// <summary>Observes the wrapper response fault when the caller reads only headers, with or without disposal.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeadersOnlyObservesTheUnaryResponseFault(bool dispose)
    {
        server.State.HoldSecondPage = true;
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1 });
        await WarmConnectionAsync(client);
        Collect();
        int unobserved = 0;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception.ToString().Contains("ClientCallInvoker", StringComparison.Ordinal)) Interlocked.Increment(ref unobserved);
        }
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            for (int i = 0; i < 5; i++) await HeadersOnly(client, dispose);
            Collect();
            Assert.Equal(0, unobserved);
        }
        finally { TaskScheduler.UnobservedTaskException -= OnUnobserved; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task HeadersOnly(UtxorpcClient client, bool dispose)
    {
        AsyncUnaryCall<Beta.Query.SearchUtxosResponse> call = client.V1Beta.Query.SearchUtxosAsync(new() { StartToken = "p2" },
            deadline: DateTime.UtcNow.AddMilliseconds(200), cancellationToken: cancellation.Token);
        try
        {
            Assert.Equal(StatusCode.DeadlineExceeded, (await Assert.ThrowsAsync<RpcException>(() => call.ResponseHeadersAsync)).StatusCode);
        }
        finally { if (dispose) call.Dispose(); }
    }

    private static void Collect()
    {
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
    }

    // CORS must expose Grpc-Encoding for compressed browser answers; Dingo v0.94.1 compresses from 1 KiB but hides it.
    // The README shows this hook for browsers; the SDK keeps the default compression.
    /// <summary>Lets the channel hook disable the compression offered by default.</summary>
    [Fact]
    public async Task CompressionCanBeTurnedOffInTheHook()
    {
        using ReadTipHandler handler = new();
        using UtxorpcClient defaults = new(new UtxorpcClientOptions
        {
            Url = server.Url,
            ConfigureChannel = options => options.HttpHandler = handler
        });
        await defaults.V1Beta.Sync.ReadTipAsync(cancellation.Token);
        Assert.Contains("gzip", handler.AcceptEncoding);
        using UtxorpcClient uncompressed = new(new UtxorpcClientOptions
        {
            Url = server.Url,
            ConfigureChannel = options => { options.HttpHandler = handler; options.CompressionProviders = []; }
        });
        await uncompressed.V1Beta.Sync.ReadTipAsync(cancellation.Token);
        Assert.Equal("identity,", handler.AcceptEncoding);
    }

    /// <summary>Checks that disabling retry leaves short and raw calls with the same allocation cost.</summary>
    [Fact]
    public async Task RetryOffShortMethodAllocatesLikeTheRawCall()
    {
        // A synchronous stub removes background HTTP/2 server allocations, so equality is exact.
        using ReadTipHandler handler = new();
        using UtxorpcClient client = new(new UtxorpcClientOptions
        {
            Url = server.Url,
            MaxAttempts = 1,
            ConfigureChannel = options => options.HttpHandler = handler
        });
        (double[] shortRounds, double[] rawRounds, int calls) = await MeasureAsync(client, true, client, handler);
        output.WriteLine($"Retry off: short rounds {string.Join(", ", shortRounds)}; raw rounds {string.Join(", ", rawRounds)} B/call.");
        Assert.Equal(rawRounds, shortRounds);
        Assert.Equal(5200, calls);
        Assert.Equal(5200, handler.Calls);
        Assert.True(server.State.Calls.IsEmpty);
    }

    /// <summary>Checks the default retry overhead against the independently measured 1.25 KB per call.</summary>
    [Fact]
    public async Task RetryOnCostsTheMeasuredExtra()
    {
        using UtxorpcClient retry = new(new UtxorpcClientOptions { Url = server.Url });
        using UtxorpcClient plain = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1 });
        (double[] retryRounds, double[] plainRounds, int calls) = await MeasureAsync(retry, false, plain);
        // Each round measures both clients back to back; the median of the five differences ignores two noisy rounds.
        double extra = retryRounds.Zip(plainRounds, static (on, off) => on - off).Order().ElementAt(2);
        output.WriteLine($"Raw: retry on {string.Join(", ", retryRounds)}; retry off {string.Join(", ", plainRounds)} B/call; median extra {extra:F3} B/call.");
        // The measured +1,248/+1,259 B baseline allows about -250/+350 B of transport noise.
        Assert.InRange(extra, 1000, 1600);
        Assert.Equal(5200, calls);
        Assert.True(server.State.Calls.IsEmpty);
    }

    private async Task<(double[], double[], int)> MeasureAsync(UtxorpcClient first, bool shortMethod, UtxorpcClient second, ReadTipHandler? handler = null)
    {
        using CancellationTokenSource measurement = new(TimeSpan.FromSeconds(30));
        int calls = await ReadTipsAsync(first, shortMethod, 100, handler, measurement.Token);
        calls += await ReadTipsAsync(second, false, 100, handler, measurement.Token);
        if (handler is not null) Assert.Equal(200, handler.Calls);
        double[] firstRounds = new double[5];
        double[] secondRounds = new double[5];
        for (int round = 0; round < 5; round++)
        {
            int beforeRound = handler?.Calls ?? 0;
            // Alternate order so transport warm-up cannot consistently favor either call.
            for (int loop = 0; loop < 2; loop++)
            {
                bool measureFirst = (round + loop) % 2 == 1;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                // With the synchronous stub every measured call completes on this thread, so its own count is exact.
                long before = handler is null ? GC.GetTotalAllocatedBytes(true) : GC.GetAllocatedBytesForCurrentThread();
                calls += await ReadTipsAsync(measureFirst ? first : second, measureFirst && shortMethod, 500, handler, measurement.Token, measured: true);
                double bytes = ((handler is null ? GC.GetTotalAllocatedBytes(true) : GC.GetAllocatedBytesForCurrentThread()) - before) / 500.0;
                if (measureFirst) firstRounds[round] = bytes;
                else secondRounds[round] = bytes;
            }
            if (handler is not null) Assert.Equal(1000, handler.Calls - beforeRound);
        }
        return (firstRounds, secondRounds, calls);
    }

    private async Task<int> ReadTipsAsync(UtxorpcClient client, bool shortMethod, int count, ReadTipHandler? handler, CancellationToken cancellationToken, bool measured = false)
    {
        ulong slots = 0;
        int calls = 0;
        for (int i = 0; i < count; i++)
        {
            Task<Beta.Sync.ReadTipResponse> pending = shortMethod
                ? client.V1Beta.Sync.ReadTipAsync(cancellationToken)
                : client.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: cancellationToken).ResponseAsync;
            if (measured && handler is not null) Assert.True(pending.IsCompletedSuccessfully, "A stub call completed asynchronously, so a per-thread count would miss bytes.");
            Beta.Sync.ReadTipResponse response = await pending;
            slots += response.Tip.Slot;
            if (handler is null)
            {
                // Consume each observation so the fake's growing queue is not benchmarked.
                Assert.True(server.State.Calls.TryDequeue(out FakeCall? call));
                Assert.Equal(ReadTipPath, call.Method);
            }
            calls++;
        }
        Assert.Equal(42UL * (ulong)count, slots);
        return calls;
    }

    private sealed class ReadTipHandler : HttpMessageHandler
    {
        private readonly byte[] responseBytes;
        public int Calls { get; private set; }
        public string AcceptEncoding { get; private set; } = "";

        public ReadTipHandler()
        {
            Beta.Sync.ReadTipResponse response = new() { Tip = new() { Slot = 42 } };
            responseBytes = new byte[5 + response.CalculateSize()];
            BinaryPrimitives.WriteInt32BigEndian(responseBytes.AsSpan(1, 4), responseBytes.Length - 5);
            response.WriteTo(responseBytes.AsSpan(5));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(HttpVersion.Version20, request.Version);
            Assert.Equal(ReadTipPath, request.RequestUri!.AbsolutePath);
            Calls++;
            AcceptEncoding = string.Join(",", request.Headers.GetValues("grpc-accept-encoding"));
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                RequestMessage = request,
                Content = new ByteArrayContent(responseBytes)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "0");
            return Task.FromResult(response);
        }
    }
}
