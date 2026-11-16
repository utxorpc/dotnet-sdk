using System.Reflection;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using ServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Utxorpc.Sdk.Test.Fakes;
using Xunit.Abstractions;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test;

/// <summary>Starts a fresh server and timeout for each client test case.</summary>
public abstract class ClientTestBase : IAsyncLifetime
{
    private protected FakeServer server = null!;
    private protected readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));

    /// <summary>Starts a fresh HTTP/2 server for each case.</summary>
    public async Task InitializeAsync() => server = await FakeServer.StartAsync();

    /// <summary>Releases the server and the test timeout.</summary>
    public async Task DisposeAsync()
    {
        cancellation.Dispose();
        await server.DisposeAsync();
    }

}

/// <summary>Checks channel defaults, retry boundaries, transport ownership and client registration.</summary>
[Trait("Category", "Unit")]
public sealed class ClientOptionsTests : ClientTestBase
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
        RpcException error = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            if (version == "v1alpha") await client.V1Alpha.Submit.SubmitTxAsync(new byte[] { 1 }, cancellation.Token);
            else await client.V1Beta.Submit.SubmitTxAsync(new byte[] { 1 }, cancellation.Token);
        });
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
        RpcException error = await Assert.ThrowsAsync<RpcException>(() => client.V1Beta.Sync.ReadTipAsync(cancellation.Token));
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        AssertCalls("/utxorpc.v1beta.sync.SyncService/ReadTip", 1);
    }

    /// <summary>Surfaces a broken stream without replaying any delivered message.</summary>
    [Fact]
    public async Task StreamIsNotRetriedAfterItsFirstMessage()
    {
        server.State.FailAfterFirstMessage = true;
        using UtxorpcClient client = new(server.Url);
        List<ulong> slots = new();
        RpcException error = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (V1Beta.FollowTipEvent response in client.V1Beta.Sync.FollowTipAsync(null, cancellationToken: cancellation.Token))
                slots.Add(response.Response.Reset.Slot);
        });
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
        RpcException error = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (V1Beta.FollowTipEvent _ in client.V1Beta.Sync.FollowTipAsync(null, cancellationToken: cancellation.Token)) { }
        });
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
            (string? Service, string? Method)[] expected = typeof(Beta.Sync.SyncService).Assembly.GetTypes()
                .Select(type => type.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static))
                .Where(property => property?.PropertyType == typeof(ServiceDescriptor))
                .Select(property => (ServiceDescriptor)property!.GetValue(null)!)
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
    public void InvalidOptionsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new UtxorpcClient((UtxorpcClientOptions)null!));
        Assert.Throws<ArgumentNullException>(() => new UtxorpcClient(new UtxorpcClientOptions()));
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
        Assert.Empty(server.State.Calls);
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
