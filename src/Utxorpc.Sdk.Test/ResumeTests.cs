using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Google.Protobuf;
using Grpc.Core;
using Utxorpc.Sdk.Test.Fakes;
using Xunit.Abstractions;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks reconnect placement, replay suppression, rollback and retry budgets over HTTP/2.</summary>
[Collection("Allocation")]
[Trait("Category", "Unit")]
public sealed class ResumeTests(ITestOutputHelper output) : ClientTestBase
{
    private readonly InstantTime time = new();

    public static TheoryData<string, bool, bool> ConnectFailures()
    {
        TheoryData<string, bool, bool> cases = new();
        foreach (string failure in new[] { "refused", "unknown host", "server stopped", "connect timeout" })
            foreach (bool watch in new[] { false, true })
                foreach (bool resume in new[] { false, true })
                    cases.Add(failure, watch, resume);
        return cases;
    }

    [Theory]
    [MemberData(nameof(ConnectFailures))]
    public async Task ConnectFailureMatchesUnaryAndHonorsResumeAttempts(string failure, bool watch, bool resume)
    {
        const int resumeAttempts = 2;
        using Socket? refused = failure == "refused" ? new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) : null;
        // Reserve the port without listening so the kernel refuses connections and no other test can take it.
        refused?.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        string url = failure switch
        {
            "refused" => $"http://{refused!.LocalEndPoint}",
            "unknown host" => "http://utxorpc.invalid",
            _ => server.Url
        };
        using StartedCallCounter starts = new();
        using TimedOutConnectTransport? timeout = failure == "connect timeout" ? new() : null;
        using UtxorpcClient client = Client(configure: options =>
        {
            options.Url = url;
            options.ResumeOnFailure = resume;
            options.ResumeAttempts = resumeAttempts;
            options.ConfigureChannel = channel =>
            {
                channel.LoggerFactory = starts;
                if (timeout is not null) channel.HttpHandler = timeout;
                else ((SocketsHttpHandler)channel.HttpHandler!).UseProxy = false;
            };
        });
        if (failure == "server stopped")
        {
            await DrainStream(client, watch, cancellation.Token, () => false);
            Assert.Single(server.State.Calls);
            await server.StopAsync(cancellation.Token);
        }

        UtxorpcException unary = await Assert.ThrowsAsync<UtxorpcException>(async () =>
            await client.V1Beta.Sync.ReadTipAsync(cancellationToken: cancellation.Token));
        Assert.Equal(UtxorpcErrorKind.Unavailable, unary.Kind);
        int before = starts.Calls;
        UtxorpcException stream = await Assert.ThrowsAsync<UtxorpcException>(() => DrainStream(client, watch, cancellation.Token));
        Assert.Equal(unary.Kind, stream.Kind);
        Assert.Equal(unary.StatusCode, stream.StatusCode);
        Assert.Equal(UtxorpcException.IsTransient(unary), UtxorpcException.IsTransient(stream));
        Assert.Equal(1 + (resume ? resumeAttempts : 0), starts.Calls - before);
        Assert.Equal(resume ? resumeAttempts : 0, time.Delays.Count);
    }

    public static TheoryData<string, bool, bool> CancellationPhases()
    {
        TheoryData<string, bool, bool> cases = new();
        foreach (string phase in new[] { "before connect", "during connect", "mid-stream" })
            foreach (bool watch in new[] { false, true })
                foreach (bool resume in new[] { false, true })
                    cases.Add(phase, watch, resume);
        return cases;
    }

    [Theory]
    [MemberData(nameof(CancellationPhases))]
    public async Task CallerCancellationKeepsItsToken(string phase, bool watch, bool resume)
    {
        using CancellationTokenSource caller = new();
        TaskCompletionSource enteredConnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using UtxorpcClient client = Client(configure: options =>
        {
            options.ResumeOnFailure = resume;
            options.ConfigureChannel = channel =>
            {
                SocketsHttpHandler sockets = (SocketsHttpHandler)channel.HttpHandler!;
                sockets.UseProxy = false;
                if (phase != "mid-stream") sockets.ConnectCallback = (_, ct) =>
                {
                    enteredConnect.TrySetResult();
                    return TimedOutConnectTransport.WaitForCancellation(ct);
                };
            };
        });
        if (phase == "before connect") caller.Cancel();
        Func<bool>? onEvent = null;
        if (phase == "mid-stream")
        {
            onEvent = () =>
            {
                caller.Cancel();
                return true;
            };
        }
        Task read = DrainStream(client, watch, caller.Token, onEvent);
        try
        {
            if (phase == "during connect")
            {
                await enteredConnect.Task.WaitAsync(cancellation.Token);
                caller.Cancel();
            }
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(cancellation.Token));
            Assert.Equal(caller.Token, error.CancellationToken);
            if (phase == "before connect") Assert.False(enteredConnect.Task.IsCompleted);
            if (phase == "mid-stream") Assert.Single(server.State.Calls);
            Assert.Empty(time.Delays);
        }
        finally { caller.Cancel(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ServerDeadlineAfterFirstEventKeepsItsException(bool watch, bool resume)
    {
        if (watch) Script(false, [W(1, 1), StatusCode.DeadlineExceeded]);
        else Script(false, [F(1), StatusCode.DeadlineExceeded]);
        using UtxorpcClient client = Client(configure: options => options.ResumeOnFailure = resume);
        int events = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => DrainStream(client, watch, cancellation.Token, () =>
        {
            events++;
            return true;
        }));
        Assert.Equal(1, events);
        Assert.Single(server.State.Calls);
        Assert.Empty(time.Delays);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ServerCancelledAfterFirstEventIsTypedAndKeepsTrailers(bool watch, bool resume)
    {
        RpcException failure = new(new Status(StatusCode.Cancelled, "stream ended"), new Metadata { { "test-detail", "server trailer" } });
        if (watch) Script(false, [W(1, 1), failure]);
        else Script(false, [F(1), failure]);
        using UtxorpcClient client = Client(configure: options => options.ResumeOnFailure = resume);
        int events = 0;
        UtxorpcException error = await Failure(() => DrainStream(client, watch, cancellation.Token, () =>
        {
            events++;
            return true;
        }), UtxorpcErrorKind.Other);
        Assert.Equal(StatusCode.Cancelled, error.StatusCode);
        Assert.Equal("stream ended", error.Detail);
        Assert.Equal("server trailer", error.Trailers.GetValue("test-detail"));
        Assert.False(UtxorpcException.IsTransient(error));
        Assert.Equal(1, events);
        Assert.Single(server.State.Calls);
        Assert.Empty(time.Delays);
    }

    [Fact]
    public async Task ClientDisposalDuringReadIsTypedAsCancelled()
    {
        Script(false, [F(1)]);
        using UtxorpcClient client = Client();
        await using IAsyncEnumerator<V1Beta.FollowTipEvent> read = client.V1Beta.Sync.FollowTipAsync(cancellationToken: cancellation.Token).GetAsyncEnumerator();
        Assert.True(await read.MoveNextAsync());
        Task<bool> pending = read.MoveNextAsync().AsTask();
        Assert.False(pending.IsCompleted);
        client.Dispose();
        UtxorpcException error = await Failure(() => pending, UtxorpcErrorKind.Other);
        Assert.Equal(StatusCode.Cancelled, error.StatusCode);
        Assert.Single(server.State.Calls);
        Assert.Empty(time.Delays);
    }

    [Fact]
    public async Task MidStreamCutCountsFailedReconnects()
    {
        const int resumeAttempts = 2;
        Script(false, [F(1)]);
        using StartedCallCounter starts = new();
        using UtxorpcClient client = Client(configure: options =>
        {
            options.ResumeAttempts = resumeAttempts;
            options.ConfigureChannel = channel => channel.LoggerFactory = starts;
        });
        await using IAsyncEnumerator<V1Beta.FollowTipEvent> read = client.V1Beta.Sync.FollowTipAsync(cancellationToken: cancellation.Token).GetAsyncEnumerator();
        Assert.True(await read.MoveNextAsync());
        Task<bool> pending = read.MoveNextAsync().AsTask();
        // Stop listening before cutting the open stream; a cancelled shutdown skips the graceful drain.
        await server.StopAsync(new CancellationToken(canceled: true));
        UtxorpcException error = await Failure(() => pending);
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.Equal(1 + resumeAttempts, starts.Calls);
        Assert.Equal(resumeAttempts, time.Delays.Count);
        Assert.Single(server.State.Calls);
    }

    private static Task DrainStream(UtxorpcClient client, bool watch, CancellationToken token, Func<bool>? onEvent = null) => watch
        ? Drain(client.V1Beta.Watch.WatchTxAsync(cancellationToken: token), onEvent)
        : Drain(client.V1Beta.Sync.FollowTipAsync(cancellationToken: token), onEvent);

    private static async Task Drain<T>(IAsyncEnumerable<T> stream, Func<bool>? onEvent)
    {
        await foreach (T _ in stream)
            if (onEvent is not null && !onEvent()) return;
    }

    [Fact]
    public async Task OffByDefaultTheFailureIsThrown()
    {
        Script(false, [F(1), F(2), StatusCode.Unavailable]);
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1, TimeProvider = time });
        await Failure(() => Follow(client, false, 3));
        Assert.Single(server.State.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ACleanReconnectYieldsNothingTwice(bool alpha, bool naming)
    {
        Script(alpha, [F(1), F(2), F(3), StatusCode.Unavailable], naming ? [F(3, "reset"), F(3), F(4), F(5)] : [F(4), F(5)]);
        using UtxorpcClient client = Client();
        Assert.Equal(["a1", "a2", "a3", "a4", "a5"], Names(await Follow(client, alpha, 5)));
        Points(3, 2, 1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ARollbackWhileDisconnectedYieldsOneReset(bool alpha, bool naming)
    {
        Script(alpha, [F(1), F(2), F(3), StatusCode.Unavailable], naming ? [F(2, "reset"), F(3, fork: true), F(4, fork: true)] : [F(3, fork: true), F(4, fork: true)]);
        using UtxorpcClient client = Client();
        List<Beta.Sync.FollowTipResponse> seen = await Follow(client, alpha, 6);
        Assert.Equal(["a1", "a2", "a3", "r2", "a3x", "a4x"], Names(seen));
        Assert.Equal(Hash(2), seen[3].Reset.Hash);
        Assert.Equal(2UL, seen[3].Reset.Slot);
        Assert.Equal(2UL, seen[3].Reset.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ResumesFromOneKnownPoint(int shape)
    {
        Beta.Sync.FollowTipResponse first = shape == 0 ? new() { Reset = new() { Slot = 5, Hash = Hash(5) } } : F(5);
        Script(false, [first, StatusCode.Unavailable], shape switch
        {
            0 => [F(5, "reset"), F(6)],
            1 => [F(6)],
            _ => [StatusCode.Unknown]
        });
        using UtxorpcClient client = Client();
        if (shape == 2)
        {
            server.State.FailDetail = "nil point returned";
            UtxorpcException error = await Failure(() => Follow(client, false, 2), UtxorpcErrorKind.Other);
            Assert.Equal(StatusCode.Unknown, error.StatusCode);
        }
        else Assert.Equal(shape == 0 ? ["r5", "a6"] : ["a5", "a6"], Names(await Follow(client, false, 2, new BlockPoint(5, Hash(5).Memory))));
        Points(5);
        Assert.Equal(2, server.State.Calls.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ABreakInsideABlockLosesAndRepeatsNothing(bool alpha, bool replayIntersect)
    {
        object[] resumed = [W(2, 2), W(2, 3), W(2, 4), W(3, 5)];
        Script(alpha, [W(1, 1), W(2, 2), W(2, 3), StatusCode.Unavailable], replayIntersect ? [W(1, 1), .. resumed] : resumed);
        using UtxorpcClient client = Client();
        Assert.Equal(["a1", "a2", "a3", "a4", "a5"], Names(await Watch(client, alpha, 5)));
        Points(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnIdleAtTheIntersectDoesNotUndoTransactionsOrRestartTheCount(bool alpha)
    {
        Beta.Watch.WatchTxResponse idle = W(2, 0, "idle");
        Script(alpha, [W(1, 1), idle, W(3, 3), StatusCode.Unavailable], [idle, StatusCode.Unavailable], [idle, W(3, 3), W(3, 4), W(4, 5)]);
        using UtxorpcClient client = Client();
        Assert.Equal(["a1", "i2", "a3", "a4", "a5"], Names(await Watch(client, alpha, 5)));
        Points(2, 1);
        Assert.Equal(2, time.Delays.Count);
        Assert.InRange(time.Delays[1].TotalSeconds, 1.6 * 0.8, 1.6 * 1.2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ARollbackWhileDisconnectedUndoesWhatWasApplied(bool alpha, bool replayIntersect)
    {
        Script(alpha, [W(1, 1), W(2, 2), W(2, 3), W(3, 4), StatusCode.Unavailable],
            replayIntersect ? [W(2, 2), W(2, 3), W(3, 5, fork: true)] : [W(2, 5, fork: true)]);
        using UtxorpcClient client = Client();
        string[] expected = replayIntersect ? ["a1", "a2", "a3", "a4", "u4", "a5"] : ["a1", "a2", "a3", "a4", "u4", "u3", "u2", "a5"];
        Assert.Equal(expected, Names(await Watch(client, alpha, expected.Length)));
        Points(2, 1);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task AnUndoDropsOnlyItsTransaction(bool alpha, bool deeper, bool replayIntersect)
    {
        object[] before = [W(2, 2), W(3, 3), W(3, 4), W(3, 3, "undo"), StatusCode.Unavailable];
        Beta.Watch.WatchTxResponse fork = W(deeper ? 2UL : 3UL, 5, fork: true);
        Script(alpha, replayIntersect ? before : [W(1, 1), .. before], replayIntersect ? [W(2, 2), fork] : [fork]);
        using UtxorpcClient client = Client();
        string[] expected = deeper ? ["a2", "a3", "a4", "u3", "u4", "u2", "a5"] : ["a2", "a3", "a4", "u3", "u4", "a5"];
        if (!replayIntersect) expected = ["a1", .. expected];
        Assert.Equal(expected, Names(await Watch(client, alpha, expected.Length)));
        Points(replayIntersect ? [2UL] : [2UL, 1UL]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReplayAtAnOlderIntersectPreservesItsTransactions(bool alpha)
    {
        Script(alpha, [W(1, 1), W(2, 2), W(3, 3), W(4, 4), StatusCode.Unavailable], [W(2, 2), W(3, 5, fork: true)]);
        using UtxorpcClient client = Client();
        Assert.Equal(["a1", "a2", "a3", "a4", "u4", "u3", "a5"], Names(await Watch(client, alpha, 7)));
        Points(3, 2, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUndoInPlaceIsPassedOn(bool alpha)
    {
        Script(alpha, [W(1, 1), W(2, 2), W(2, 2, "undo"), W(2, 3, fork: true), W(3, 0, "idle"), StatusCode.Unavailable], [W(4, 4)]);
        using UtxorpcClient client = Client();
        Assert.Equal(["a1", "a2", "u2", "a3", "i3", "a4"], Names(await Watch(client, alpha, 6)));
        Beta.Watch.WatchTxRequest request = Assert.IsType<Beta.Watch.WatchTxRequest>(AsBeta(server.State.Requests.Last()));
        Assert.Equal([3UL, 2UL, 1UL], request.Intersect.Select(p => p.Slot));
        Assert.Equal(Hash(2, true), request.Intersect[1].Hash);
    }

    [Fact]
    public async Task GivesUpAfterTenAttempts()
    {
        Script(false, [F(1), F(2), StatusCode.Unavailable], [StatusCode.Unavailable]);
        using UtxorpcClient client = Client();
        await Failure(() => Follow(client, false, 3));
        Assert.Equal(11, server.State.Calls.Count);
        Assert.Equal(10, time.Delays.Count);
        for (int i = 0; i < time.Delays.Count; i++) Assert.InRange(time.Delays[i].TotalSeconds, Math.Pow(1.6, i) * 0.8, Math.Pow(1.6, i) * 1.2);
        PrintDelays();
    }

    [Fact]
    public async Task ResumeAttemptsLimitsTheTries()
    {
        Script(false, [F(1), F(2), StatusCode.Unavailable], [StatusCode.Unavailable], [StatusCode.Unavailable], [StatusCode.NotFound]);
        using UtxorpcClient client = Client(configure: options => options.ResumeAttempts = 2);
        await Failure(() => Follow(client, false, 3));
        Assert.Equal(3, server.State.Calls.Count);
        Assert.Equal(2, time.Delays.Count);
    }

    [Fact]
    public async Task NullKeepsTryingUntilCancelled()
    {
        List<object[]> scripts = [[F(1), StatusCode.Unavailable]];
        for (int i = 0; i < 99; i++) scripts.Add([StatusCode.Unavailable]);
        scripts.Add([F(2)]);
        Script(false, scripts.ToArray());
        using UtxorpcClient client = Client(configure: options => options.ResumeAttempts = null);
        Assert.Equal(["a1", "a2"], Names(await Follow(client, false, 2)));
        Assert.Equal(100, time.Delays.Count);
        Assert.All(time.Delays, delay => Assert.InRange(delay.TotalSeconds, 0.8, 144));
        Assert.All(time.Delays.Skip(11), delay => Assert.InRange(delay.TotalSeconds, 96, 144));
        PrintDelays();
    }

    [Fact]
    public async Task AFailureAfterEveryReconnectEndsAtTheLimit()
    {
        Script(false, [W(1, 1), W(2, 2), StatusCode.Unavailable], [W(2, 2), StatusCode.Unavailable]);
        using UtxorpcClient client = Client();
        List<Beta.Watch.WatchTxResponse> seen = [];
        await Failure(() => Watch(client, false, 3, seen));
        Assert.Equal(["a1", "a2"], Names(seen));
        Assert.Equal(11, server.State.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARepeatedIdleIsNotYieldedAgain(bool alpha)
    {
        Script(alpha, [W(1, 0, "idle"), W(2, 0, "idle"), W(3, 0, "idle"), StatusCode.Unavailable], [W(3, 0, "idle"), W(4, 0, "idle")]);
        using UtxorpcClient client = Client();
        Assert.Equal(["i1", "i2", "i3", "i4"], Names(await Watch(client, alpha, 4)));
        Points(3, 2, 1);
    }

    [Fact]
    public async Task AnIdleReplayDoesNotRestartTheCount()
    {
        Script(false, [W(1, 0, "idle"), W(2, 0, "idle"), W(3, 0, "idle"), StatusCode.Unavailable], [W(3, 0, "idle"), StatusCode.Unavailable]);
        using UtxorpcClient client = Client(configure: options => options.ResumeAttempts = 2);
        List<Beta.Watch.WatchTxResponse> seen = [];
        await Failure(() => Watch(client, false, 4, seen));
        Assert.Equal(["i1", "i2", "i3"], Names(seen));
        Assert.Equal(3, server.State.Calls.Count);
        Assert.Equal(2, time.Delays.Count);
    }

    [Fact]
    public async Task TheFirstNewEventRestartsTheCount()
    {
        List<object[]> scripts = [];
        for (int i = 0; i < 10; i++) scripts.Add([StatusCode.Unavailable]);
        scripts.Add([F(1), F(2), StatusCode.Unavailable]);
        for (int i = 0; i < 9; i++) scripts.Add([StatusCode.Unavailable]);
        scripts.Add([F(3)]);
        Script(false, scripts.ToArray());
        using UtxorpcClient client = Client();
        Assert.Equal(["a1", "a2", "a3"], Names(await Follow(client, false, 3)));
        Assert.Equal(20, time.Delays.Count);
        Assert.InRange(time.Delays[10].TotalSeconds, 0.8, 1.2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyTransientFailuresResume(bool unreadable)
    {
        Beta.Sync.FollowTipResponse nested = F(3);
        Beta.Cardano.PlutusData datum = new() { BoundedBytes = ByteString.CopyFrom(1) };
        for (int i = 0; i < 60; i++) datum = new() { Array = new() { Items = { datum } } };
        nested.Apply.Cardano.Body = new() { Tx = { new Beta.Cardano.Tx { Outputs = { new Beta.Cardano.TxOutput { Datum = new() { Payload = datum } } } } } };
        Script(false, [F(1), F(2), unreadable ? nested : StatusCode.NotFound]);
        using UtxorpcClient client = Client();
        UtxorpcException error = await Failure(() => Follow(client, false, 3), unreadable ? UtxorpcErrorKind.Other : UtxorpcErrorKind.NotFound);
        Assert.Equal(unreadable ? StatusCode.Unavailable : StatusCode.NotFound, error.StatusCode);
        Assert.Single(server.State.Calls);
    }

    [Fact]
    public async Task CancellationDuringTheWaitStops()
    {
        Script(false, [F(1), StatusCode.Unavailable]);
        InstantTime stopped = new(fire: false);
        using UtxorpcClient client = Client(clock: stopped);
        Task<List<Beta.Sync.FollowTipResponse>> read = Follow(client, false, 2);
        await stopped.Created.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Single(server.State.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStreamWithoutBlockHeadersIsNotResumed(bool readableAfter)
    {
        Beta.Watch.WatchTxResponse headerless = new() { Apply = new() { Cardano = new() { Hash = Hash(1) } } };
        Script(false, readableAfter ? [headerless, W(1, 1), W(2, 2), StatusCode.Unavailable] : [headerless, StatusCode.Unavailable]);
        using UtxorpcClient client = Client();
        await Failure(() => Watch(client, false, readableAfter ? 4 : 2));
        Assert.Single(server.State.Calls);
    }

    [Fact]
    public async Task CancellationBeforeResumeKeepsTheTypedFailure()
    {
        using CancellationTokenSource caller = new();
        using FramedHandler handler = new(false, 0, cancelOnFailure: caller);
        using UtxorpcClient client = Client(configure: options => options.ConfigureChannel = channel => channel.HttpHandler = handler);
        // Cancel after the transport records the failure, before the SDK decides to resume: keep the typed failure without retrying.
        // If failure and cancellation occur at the same instant, either may win; this test does not pin that race.
        UtxorpcException error = await Failure(() => DrainStream(client, false, caller.Token));
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.True(caller.IsCancellationRequested);
        Assert.Empty(time.Delays);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task BeforeTheFirstEventTheSameRequestIsSentAgain()
    {
        Script(false, [StatusCode.Unavailable], [F(6)]);
        using UtxorpcClient client = Client();
        Assert.Equal(["a6"], Names(await Follow(client, false, 1, new BlockPoint(5, Hash(5).Memory))));
        IMessage[] requests = server.State.Requests.ToArray();
        Assert.Equal(requests[0], requests[1]);
        Points(5);
    }

    [Theory]
    [InlineData("height")]
    [InlineData("undo")]
    [InlineData("reset")]
    public async Task AnUnplaceableReconnectThrowsTheFailure(string shape)
    {
        Script(false, [F(1), F(2), F(3), StatusCode.Unavailable], [F(9, shape == "height" ? "apply" : shape)]);
        using UtxorpcClient client = Client();
        List<Beta.Sync.FollowTipResponse> seen = [];
        await Failure(() => Follow(client, false, 4, seen: seen));
        Assert.Equal(["a1", "a2", "a3"], Names(seen));
        Assert.Equal(2, server.State.Calls.Count);
    }

    [Fact]
    public async Task AnAbortedConnectionResumes()
    {
        Script(false, [F(1), F(2), F(3), FakeState.Abort], [F(4), F(5)]);
        using UtxorpcClient client = Client();
        Assert.Equal(["a1", "a2", "a3", "a4", "a5"], Names(await Follow(client, false, 5)));
        Points(3, 2, 1);
    }

    [Fact]
    public async Task TheFirstWatchTxBlockAloneIsNotResumed()
    {
        Script(false, [W(1, 1), StatusCode.Unavailable]);
        using UtxorpcClient client = Client();
        await Failure(() => Watch(client, false, 2));
        Assert.Single(server.State.Calls);
    }

    [Fact]
    public async Task ResumeAllocatesNothingPerEvent()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            foreach (bool watch in new[] { false, true })
            {
                long[] totals = new long[4];
                for (int mode = 0; mode < totals.Length; mode++)
                {
                    int count = mode % 2 == 0 ? 1 : 31;
                    using FramedHandler handler = new(watch, count);
                    using UtxorpcClient client = new(new UtxorpcClientOptions
                    {
                        Url = "http://localhost",
                        MaxAttempts = 1,
                        ResumeOnFailure = mode >= 2,
                        TimeProvider = time,
                        ConfigureChannel = options => options.HttpHandler = handler
                    });
                    for (int i = 0; i < 100; i++) await Read();
                    totals[mode] = long.MaxValue;
                    for (int run = 0; run < 3; run++)
                    {
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        for (int i = 0; i < 1000; i++) await Read(measured: true);
                        totals[mode] = Math.Min(totals[mode], GC.GetAllocatedBytesForCurrentThread() - before);
                    }

                    async Task Read(bool measured = false)
                    {
                        if (watch)
                        {
                            IAsyncEnumerator<V1Beta.WatchTxEvent> read = client.V1Beta.Watch.WatchTxAsync(cancellationToken: cancellation.Token).GetAsyncEnumerator();
                            await using (read)
                                while (true)
                                {
                                    ValueTask<bool> next = read.MoveNextAsync();
                                    bool synchronous = next.IsCompletedSuccessfully;
                                    bool found = await next;
                                    Assert.True(!measured || synchronous, $"The allocation stub must complete synchronously (watch={watch}, count={count}, found={found}).");
                                    if (!found) break;
                                }
                        }
                        else
                        {
                            IAsyncEnumerator<V1Beta.FollowTipEvent> read = client.V1Beta.Sync.FollowTipAsync(cancellationToken: cancellation.Token).GetAsyncEnumerator();
                            await using (read)
                                while (true)
                                {
                                    ValueTask<bool> next = read.MoveNextAsync();
                                    bool synchronous = next.IsCompletedSuccessfully;
                                    bool found = await next;
                                    Assert.True(!measured || synchronous, $"The allocation stub must complete synchronously (watch={watch}, count={count}, found={found}).");
                                    if (!found) break;
                                }
                        }
                    }
                }
                double off = (totals[1] - totals[0]) / 30000.0, on = (totals[3] - totals[2]) / 30000.0;
                output.WriteLine($"{(watch ? "WatchTx" : "FollowTip")}: off(1)={totals[0]}, off(31)={totals[1]}, on(1)={totals[2]}, on(31)={totals[3]}; per event off={off}, on={on} B.");
                if (!watch) Assert.Equal(off, on);
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private UtxorpcClient Client(InstantTime? clock = null, Action<UtxorpcClientOptions>? configure = null)
    {
        UtxorpcClientOptions options = new() { Url = server.Url, ResumeOnFailure = true, MaxAttempts = 1, TimeProvider = clock ?? time };
        configure?.Invoke(options);
        return new(options);
    }

    private void Script(bool alpha, params object[][] scripts)
    {
        foreach (object[] script in scripts)
            server.State.StreamScripts.Add(script.Select(step => alpha ? step switch
            {
                Beta.Sync.FollowTipResponse f => (object)Alpha.Sync.FollowTipResponse.Parser.ParseFrom(f.ToByteArray()),
                Beta.Watch.WatchTxResponse w => Alpha.Watch.WatchTxResponse.Parser.ParseFrom(w.ToByteArray()),
                _ => step
            } : step).ToArray());
    }

    private async Task<List<Beta.Sync.FollowTipResponse>> Follow(UtxorpcClient client, bool alpha, int count, BlockPoint? point = null, List<Beta.Sync.FollowTipResponse>? seen = null)
    {
        seen ??= [];
        if (alpha)
        {
            await foreach (V1Alpha.FollowTipEvent item in client.V1Alpha.Sync.FollowTipAsync(point, cancellationToken: cancellation.Token))
            {
                seen.Add(Beta.Sync.FollowTipResponse.Parser.ParseFrom(item.Response.ToByteArray()));
                if (seen.Count == count) break;
            }
        }
        else
        {
            await foreach (V1Beta.FollowTipEvent item in client.V1Beta.Sync.FollowTipAsync(point, cancellationToken: cancellation.Token))
            {
                seen.Add(item.Response);
                if (seen.Count == count) break;
            }
        }
        return seen;
    }

    private async Task<List<Beta.Watch.WatchTxResponse>> Watch(UtxorpcClient client, bool alpha, int count, List<Beta.Watch.WatchTxResponse>? seen = null)
    {
        seen ??= [];
        if (alpha)
        {
            await foreach (V1Alpha.WatchTxEvent item in client.V1Alpha.Watch.WatchTxAsync(cancellationToken: cancellation.Token))
            {
                seen.Add(Beta.Watch.WatchTxResponse.Parser.ParseFrom(item.Response.ToByteArray()));
                if (seen.Count == count) break;
            }
        }
        else
        {
            await foreach (V1Beta.WatchTxEvent item in client.V1Beta.Watch.WatchTxAsync(cancellationToken: cancellation.Token))
            {
                seen.Add(item.Response);
                if (seen.Count == count) break;
            }
        }
        return seen;
    }

    private static async Task<UtxorpcException> Failure(Func<Task> read, UtxorpcErrorKind kind = UtxorpcErrorKind.Unavailable)
    {
        UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(read);
        Assert.Equal(kind, error.Kind);
        return error;
    }

    private void Points(params ulong[] expected)
    {
        IMessage request = AsBeta(server.State.Requests.Last());
        IEnumerable<(ulong Slot, ByteString Hash)> points = request switch
        {
            Beta.Sync.FollowTipRequest follow => follow.Intersect.Select(p => (p.Slot, p.Hash)),
            Beta.Watch.WatchTxRequest watch => watch.Intersect.Select(p => (p.Slot, p.Hash)),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expected.Select(n => (n, Hash(n))), points);
    }

    private static IMessage AsBeta(IMessage request) => request switch
    {
        Alpha.Sync.FollowTipRequest f => Beta.Sync.FollowTipRequest.Parser.ParseFrom(f.ToByteArray()),
        Alpha.Watch.WatchTxRequest w => Beta.Watch.WatchTxRequest.Parser.ParseFrom(w.ToByteArray()),
        _ => request
    };

    private void PrintDelays() => output.WriteLine($"Delays (seconds): {string.Join(", ", time.Delays.Select(d => d.TotalSeconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)))}");

    private static ByteString Hash(ulong n, bool fork = false) => ByteString.CopyFrom(Enumerable.Repeat((byte)(n + (fork ? 100UL : 0)), 32).ToArray());

    private static Beta.Sync.FollowTipResponse F(ulong n, string action = "apply", bool fork = false)
    {
        Beta.Sync.AnyChainBlock block = new() { Cardano = new() { Header = new() { Slot = n, Hash = Hash(n, fork), Height = n } } };
        return action switch
        {
            "reset" => new() { Reset = new() { Slot = n, Hash = Hash(n, fork), Height = n } },
            "undo" => new() { Undo = block },
            _ => new() { Apply = block }
        };
    }

    private static Beta.Watch.WatchTxResponse W(ulong n, byte tx, string action = "apply", bool fork = false)
    {
        Beta.Watch.AnyChainTx message = new() { Block = new() { Cardano = F(n, fork: fork).Apply.Cardano }, Cardano = new() { Hash = Hash(tx) } };
        return action switch
        {
            "idle" => new() { Idle = new() { Slot = n, Hash = Hash(n, fork), Height = n } },
            "undo" => new() { Undo = message },
            _ => new() { Apply = message }
        };
    }

    private static IEnumerable<string> Names(IEnumerable<Beta.Sync.FollowTipResponse> messages) => messages.Select(m => m.Reset is { } reset ? $"r{reset.Slot}" : $"a{m.Apply.Cardano.Header.Slot}{(m.Apply.Cardano.Header.Hash[0] > 100 ? "x" : "")}");
    private static IEnumerable<string> Names(IEnumerable<Beta.Watch.WatchTxResponse> messages) => messages.Select(m => m.Idle is { } idle ? $"i{idle.Slot}" : $"{(m.Apply is not null ? "a" : "u")}{(m.Apply ?? m.Undo).Cardano.Hash[0]}");

    private sealed class InstantTime(bool fire = true) : TimeProvider
    {
        internal List<TimeSpan> Delays { get; } = [];
        internal TaskCompletionSource Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            Created.TrySetResult();
            if (fire) callback(state);
            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FramedHandler : HttpMessageHandler
    {
        private readonly byte[] bytes;
        private readonly CancellationTokenSource? cancelOnFailure;
        internal int Calls { get; private set; }

        internal FramedHandler(bool watch, int count, CancellationTokenSource? cancelOnFailure = null)
        {
            this.cancelOnFailure = cancelOnFailure;
            using MemoryStream body = new();
            Span<byte> header = stackalloc byte[5];
            header[0] = 0;
            for (ulong i = 1; i <= (ulong)count; i++)
            {
                IMessage message = watch ? W(i, (byte)i) : F(i);
                byte[] payload = message.ToByteArray();
                BinaryPrimitives.WriteInt32BigEndian(header[1..], payload.Length);
                body.Write(header);
                body.Write(payload);
            }
            bytes = body.ToArray();
        }

        private sealed class CancelOnDisposeResponse(CancellationTokenSource stop) : HttpResponseMessage(HttpStatusCode.OK)
        {
            private readonly Lock cancelGate = new();

            protected override void Dispose(bool disposing)
            {
                // Run on both cleanup paths, even if another thread already started disposing the response.
                if (disposing)
                {
                    lock (cancelGate)
                    {
                        if (!stop.IsCancellationRequested) stop.Cancel();
                    }
                }
                base.Dispose(disposing);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            HttpResponseMessage response = cancelOnFailure is null
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new CancelOnDisposeResponse(cancelOnFailure);
            response.Version = HttpVersion.Version20;
            response.RequestMessage = request;
            response.Content = new ByteArrayContent(bytes);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", cancelOnFailure is null ? "0" : "14");
            return Task.FromResult(response);
        }
    }
}
