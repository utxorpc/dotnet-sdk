using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.Sdk.Test.Fakes;

namespace Utxorpc.Sdk.Test;

/// <summary>Exercises the shared client against the in-process HTTP/2 server.</summary>
[Trait("Category", "Unit")]
public sealed class UtxorpcClientTests : IAsyncLifetime
{
    private FakeServer server = null!;

    /// <summary>Supplies only the version-specific calls used by the shared tests.</summary>
    public sealed record Version(string Name,
        Func<UtxorpcClient, Metadata?, CancellationToken, Task<ulong>> ReadTipSlot,
        Func<UtxorpcClient, Metadata?, CancellationToken, IAsyncEnumerable<ulong>> FollowTipSlots);

    /// <summary>Supplies both versions without duplicating test logic.</summary>
    public static IEnumerable<object[]> Versions()
    {
        yield return new object[] { new Version("v1alpha",
            async (client, headers, ct) => (await client.V1Alpha.Sync.ReadTipAsync(new(), headers, cancellationToken: ct)).Tip.Slot,
            (client, headers, ct) => ReadSlots(client.V1Alpha.Sync.FollowTip(new(), headers, cancellationToken: ct), message => message.Reset.Slot, ct)) };
        yield return new object[] { new Version("v1beta",
            async (client, headers, ct) => (await client.V1Beta.Sync.ReadTipAsync(new(), headers, cancellationToken: ct)).Tip.Slot,
            (client, headers, ct) => ReadSlots(client.V1Beta.Sync.FollowTip(new(), headers, cancellationToken: ct), message => message.Reset.Slot, ct)) };
    }

    /// <summary>Starts a fresh server for each test case.</summary>
    public async Task InitializeAsync() => server = await FakeServer.StartAsync();
    /// <summary>Releases the server after each test case.</summary>
    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    /// <summary>Checks the answer, method path and client headers for a unary call.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task UnaryCallAnswersAndCarriesClientHeaders(Version version)
    {
        using UtxorpcClient client = new(server.Url, new Dictionary<string, string> { ["x-test"] = "1" });
        Assert.Equal(42UL, await version.ReadTipSlot(client, null, default));
        FakeCall call = Assert.Single(server.State.Calls);
        Assert.Equal($"/utxorpc.{version.Name}.sync.SyncService/ReadTip", call.Method);
        Assert.Contains(new KeyValuePair<string, string>("x-test", "1"), call.Headers);
    }

    /// <summary>Checks stream order and client headers.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task StreamYieldsEveryMessageInOrderAndCarriesClientHeaders(Version version)
    {
        using UtxorpcClient client = new(server.Url, new Dictionary<string, string> { ["x-test"] = "1" });
        List<ulong> slots = new();
        await foreach (ulong slot in version.FollowTipSlots(client, null, default))
        {
            slots.Add(slot);
            if (slots.Count == 3) break;
        }
        Assert.Equal(new ulong[] { 1, 2, 3 }, slots);
        Assert.Contains(new KeyValuePair<string, string>("x-test", "1"), Assert.Single(server.State.Calls).Headers);
    }

    /// <summary>Checks that call headers supplement rather than replace client headers.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task PerCallHeadersAreAddedToClientHeaders(Version version)
    {
        using UtxorpcClient client = new(server.Url, new Dictionary<string, string> { ["x-a"] = "1" });
        await version.ReadTipSlot(client, new Metadata { { "x-b", "2" } }, default);
        IReadOnlyList<KeyValuePair<string, string>> headers = Assert.Single(server.State.Calls).Headers;
        Assert.Contains(new KeyValuePair<string, string>("x-a", "1"), headers);
        Assert.Contains(new KeyValuePair<string, string>("x-b", "2"), headers);
    }

    /// <summary>Checks that breaking enumeration disposes the underlying call.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task LeavingTheLoopEndsTheCallOnTheServer(Version version)
    {
        using UtxorpcClient client = new(server.Url);
        await foreach (ulong _ in version.FollowTipSlots(client, null, default)) break;
        await server.State.StreamEnded.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Checks the owned channel's unary cancellation setting.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task CancelledUnaryCallThrowsOperationCanceled(Version version)
    {
        using UtxorpcClient client = new(server.Url);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => version.ReadTipSlot(client, null, cancellation.Token));
    }

    /// <summary>Checks cancellation after a stream has begun yielding responses.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task CancelledStreamThrowsOperationCanceled(Version version)
    {
        using UtxorpcClient client = new(server.Url);
        using CancellationTokenSource cancellation = new();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ulong _ in version.FollowTipSlots(client, null, cancellation.Token)) cancellation.Cancel();
        });
    }

    /// <summary>Checks that the server's gRPC status is not wrapped or translated.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task ServerStatusArrivesAsRpcException(Version version)
    {
        server.State.FailWith = StatusCode.NotFound;
        using UtxorpcClient client = new(server.Url);
        RpcException error = await Assert.ThrowsAsync<RpcException>(() => version.ReadTipSlot(client, null, default));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    /// <summary>Checks the status for a service the server does not implement.</summary>
    [Fact]
    public async Task UnmappedServiceIsUnimplemented()
    {
        using UtxorpcClient client = new(server.Url);
        RpcException error = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (Utxorpc.V1Beta.Watch.WatchTxResponse _ in client.V1Beta.Watch.WatchTx(new()).ReadAllAsync()) { }
        });
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
    }

    /// <summary>Checks both generated client families use the same HTTP/2 connection.</summary>
    [Fact]
    public async Task BothVersionsShareOneConnection()
    {
        using UtxorpcClient client = new(server.Url);
        await client.V1Alpha.Sync.ReadTipAsync(new());
        await client.V1Beta.Sync.ReadTipAsync(new());
        FakeCall[] calls = server.State.Calls.ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(calls[0].ConnectionId, calls[1].ConnectionId);
    }

    /// <summary>Checks disposal closes an owned channel and is harmless twice.</summary>
    [Fact]
    public async Task DisposeClosesTheChannelTheClientCreated()
    {
        UtxorpcClient client = new(server.Url);
        client.Dispose();
        client.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.V1Beta.Sync.ReadTipAsync(new()));
    }

    /// <summary>Checks disposal leaves a caller-owned channel usable.</summary>
    [Fact]
    public async Task DisposeLeavesACallerChannelOpen()
    {
        using GrpcChannel channel = GrpcChannel.ForAddress(server.Url);
        UtxorpcClient first = new(channel);
        first.Dispose();
        using UtxorpcClient second = new(channel);
        Assert.Equal(42UL, (await second.V1Beta.Sync.ReadTipAsync(new())).Tip.Slot);
    }

    /// <summary>Checks invalid header names fail before channel configuration.</summary>
    [Fact]
    public void InvalidHeaderNameThrows()
    {
        bool configured = false;
        Assert.Throws<ArgumentException>(() => new UtxorpcClient(server.Url, [new("bad header", "x")], _ => configured = true));
        Assert.False(configured);
    }

    /// <summary>Checks the optional callback changes actual channel behavior.</summary>
    [Fact]
    public async Task ConfigureChannelIsApplied()
    {
        using UtxorpcClient client = new(server.Url, configureChannel: options => options.MaxReceiveMessageSize = 1);
        RpcException error = await Assert.ThrowsAsync<RpcException>(async () => await client.V1Beta.Sync.ReadTipAsync(new()));
        Assert.Equal(StatusCode.ResourceExhausted, error.StatusCode);
    }

    private static async IAsyncEnumerable<ulong> ReadSlots<T>(AsyncServerStreamingCall<T> call, Func<T, ulong> slot,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (T message in call.ReadAllAsync(cancellationToken)) yield return slot(message);
    }
}
