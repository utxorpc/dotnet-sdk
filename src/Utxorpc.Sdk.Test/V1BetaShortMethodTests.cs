using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Test.Fakes;
using Query = Utxorpc.V1Beta.Query;
using Submit = Utxorpc.V1Beta.Submit;
using Sync = Utxorpc.V1Beta.Sync;
using Watch = Utxorpc.V1Beta.Watch;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks v1beta short-method requests, responses, paging and stream lifetimes.</summary>
[Trait("Category", "Unit")]
public sealed class V1BetaShortMethodTests : IAsyncLifetime
{
    private readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
    private FakeServer server = null!;
    private UtxorpcClient client = null!;
    private CancellationToken Token => cancellation.Token;

    /// <summary>Starts a fresh server and client for each case.</summary>
    public async Task InitializeAsync()
    {
        server = await FakeServer.StartAsync();
        client = new UtxorpcClient(server.Url);
    }

    /// <summary>Closes the client, token source and fake server.</summary>
    public async Task DisposeAsync()
    {
        client.Dispose();
        cancellation.Dispose();
        await server.DisposeAsync();
    }

    /// <summary>Reads the fake parameter snapshot.</summary>
    [Fact]
    public async Task ReadParamsAsync()
    {
        FieldMask mask = FieldMask.FromString("native_bytes");
        Query.ReadParamsResponse response = await client.V1Beta.Query.ReadParamsAsync(mask, Token);
        Assert.Equal(7UL, response.LedgerTip.Slot);
        Assert.Equal(mask, TakeRequest<Query.ReadParamsRequest>().FieldMask);
    }

    /// <summary>Preserves output references and their order.</summary>
    [Fact]
    public async Task ReadUtxosAsync()
    {
        byte[] hashA = [1, 2];
        byte[] hashB = [3, 4];
        FieldMask mask = FieldMask.FromString("native_bytes");
        Query.ReadUtxosResponse response = await client.V1Beta.Query.ReadUtxosAsync([new(hashA, 0), new(hashB, 3)], mask, Token);
        Query.ReadUtxosRequest request = TakeRequest<Query.ReadUtxosRequest>();
        Assert.Equal(mask, request.FieldMask);
        Assert.Equal(2, request.Keys.Count);
        Assert.True(request.Keys[0].Hash.Span.SequenceEqual(hashA));
        Assert.Equal(0U, request.Keys[0].Index);
        Assert.True(request.Keys[1].Hash.Span.SequenceEqual(hashB));
        Assert.Equal(3U, request.Keys[1].Index);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(request.Keys[0], response.Items[0].TxoRef);
        Assert.Equal(request.Keys[1], response.Items[1].TxoRef);
    }

    /// <summary>Reads three pages with default and explicit sizes, preserving token presence.</summary>
    [Fact]
    public async Task SearchAllUtxosAsync()
    {
        Query.UtxoPredicate predicate = new Query.UtxoPredicate { Match = new() { Cardano = new() } };
        FieldMask mask = FieldMask.FromString("native_bytes");
        Assert.Throws<ArgumentOutOfRangeException>(() => client.V1Beta.Query.SearchAllUtxosAsync(predicate, -1, mask, Token));
        foreach (int pageSize in new[] { 0, 2 })
        {
            List<byte> values = new();
            await foreach (Query.AnyUtxoData item in client.V1Beta.Query.SearchAllUtxosAsync(predicate, pageSize, mask, Token))
            {
                values.Add(item.NativeBytes[0]);
            }
            Assert.Equal(new byte[] { 1, 2, 3 }, values);
            for (int page = 0; page < 3; page++)
            {
                Query.SearchUtxosRequest request = TakeRequest<Query.SearchUtxosRequest>();
                Assert.Equal(mask, request.FieldMask);
                Assert.Equal(predicate, request.Predicate);
                Assert.Equal(page == 0 ? "" : $"p{page + 1}", request.StartToken);
                Assert.Equal(page > 0, request.HasStartToken);
                Assert.Equal(pageSize, request.MaxItems);
                Assert.Equal(pageSize > 0, request.HasMaxItems);
            }
            Assert.Empty(server.State.Requests);
        }
    }

    /// <summary>Builds the exact-address predicate without changing its bytes.</summary>
    [Fact]
    public async Task SearchUtxosByAddressAsync()
    {
        byte[] address = [5, 6, 7];
        FieldMask mask = FieldMask.FromString("native_bytes");
        await foreach (Query.AnyUtxoData _ in client.V1Beta.Query.SearchUtxosByAddressAsync(address, fieldMask: mask, cancellationToken: Token)) break;
        Query.SearchUtxosRequest request = TakeRequest<Query.SearchUtxosRequest>();
        Assert.Equal(mask, request.FieldMask);
        Assert.True(request.Predicate.Match.Cardano.Address.ExactAddress.Span.SequenceEqual(address));
    }

    /// <summary>Propagates cancellation while a subsequent page is pending.</summary>
    [Fact]
    public async Task CancellingWhileSecondPageIsHeldThrowsOperationCanceled()
    {
        FieldMask mask = FieldMask.FromString("native_bytes");
        server.State.HoldSecondPage = true;
        await using IAsyncEnumerator<Query.AnyUtxoData> items = client.V1Beta.Query
            .SearchAllUtxosAsync(new Query.UtxoPredicate(), fieldMask: mask, cancellationToken: Token).GetAsyncEnumerator();
        Assert.True(await items.MoveNextAsync());
        Task<bool> pendingPage = items.MoveNextAsync().AsTask();
        while (server.State.Requests.Count < 2) await Task.Delay(10, Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingPage);
        Assert.Equal(mask, TakeRequest<Query.SearchUtxosRequest>().FieldMask);
        Assert.Equal(mask, TakeRequest<Query.SearchUtxosRequest>().FieldMask);
    }

    /// <summary>Passes transaction hash bytes into the generated request.</summary>
    [Fact]
    public async Task ReadTxAsync()
    {
        byte[] hash = [8, 9, 10];
        FieldMask mask = FieldMask.FromString("native_bytes");
        Query.ReadTxResponse response = await client.V1Beta.Query.ReadTxAsync(hash, mask, Token);
        Assert.Equal(mask, TakeRequest<Query.ReadTxRequest>().FieldMask);
        Assert.True(response.Tx.NativeBytes.Span.SequenceEqual(hash));
    }

    /// <summary>Passes transaction bytes through and returns the generated response.</summary>
    [Fact]
    public async Task SubmitTxAsync()
    {
        byte[] tx = new byte[40];
        for (int i = 0; i < tx.Length; i++) tx[i] = (byte)i;
        Submit.SubmitTxResponse response = await client.V1Beta.Submit.SubmitTxAsync(tx, Token);
        Assert.True(TakeRequest<Submit.SubmitTxRequest>().Tx.Raw.Span.SequenceEqual(tx));
        Assert.True(response.Ref.Span.SequenceEqual(tx.AsSpan(0, 32)));
    }

    /// <summary>Yields the acknowledged, mempool and confirmed stages in order.</summary>
    [Fact]
    public async Task WaitForTxAsync()
    {
        byte[] hash = [11, 12, 13];
        List<Submit.Stage> stages = new();
        await foreach (Submit.WaitForTxResponse response in client.V1Beta.Submit.WaitForTxAsync(hash, Token))
        {
            Assert.True(response.Ref.Span.SequenceEqual(hash));
            stages.Add(response.Stage);
            if (stages.Count == 3) break;
        }
        Assert.Equal(new[] { Submit.Stage.Acknowledged, Submit.Stage.Mempool, Submit.Stage.Confirmed }, stages);
        Assert.True(Assert.Single(TakeRequest<Submit.WaitForTxRequest>().Ref).Span.SequenceEqual(hash));
    }

    /// <summary>Closes the server call when transaction enumeration stops early.</summary>
    [Fact]
    public async Task LeavingWaitForTxEndsTheCallOnTheServer()
    {
        await foreach (Submit.WaitForTxResponse _ in client.V1Beta.Submit.WaitForTxAsync(new byte[] { 14 }, Token))
        {
            cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            Token.ThrowIfCancellationRequested();
            break;
        }
        await server.State.WaitForStreamEndAsync("v1beta");
    }

    /// <summary>Reads the fake synchronization tip.</summary>
    [Fact]
    public async Task ReadTipAsync()
    {
        Sync.ReadTipResponse response = await client.V1Beta.Sync.ReadTipAsync(Token);
        Assert.Equal(42UL, response.Tip.Slot);
    }

    /// <summary>Builds the block reference from a plain block point.</summary>
    [Fact]
    public async Task FetchBlockAsync()
    {
        byte[] hash = [15, 16];
        FieldMask mask = FieldMask.FromString("native_bytes");
        server.State.BlockBytes = [];
        await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(5, hash), mask, Token);
        await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(5, hash), mask).WaitAsync(Token);
        await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(5, hash), fieldMask: mask, cancellationToken: Token);
        for (int i = 0; i < 3; i++)
        {
            Sync.FetchBlockRequest request = TakeRequest<Sync.FetchBlockRequest>();
            Assert.Equal(mask, request.FieldMask);
            Sync.BlockRef point = Assert.Single(request.Ref);
            Assert.Equal(5UL, point.Slot);
            Assert.True(point.Hash.Span.SequenceEqual(hash));
        }
        Assert.Empty(server.State.Requests);
    }

    /// <summary>Builds the intersection and yields all configured slots in order.</summary>
    [Fact]
    public async Task FollowTipAsync()
    {
        byte[] hash = [17, 18];
        FieldMask mask = FieldMask.FromString("native_bytes");
        List<ulong> slots = new();
        await foreach (V1Beta.FollowTipEvent response in client.V1Beta.Sync.FollowTipAsync(new BlockPoint(5, hash), mask, Token))
        {
            Assert.True(response.TryGetValue(out V1Beta.Reset reset));
            slots.Add(reset.Point.Slot);
            if (slots.Count == 3) break;
        }
        Assert.Equal(new ulong[] { 1, 2, 3 }, slots);
        Sync.FollowTipRequest request = TakeRequest<Sync.FollowTipRequest>();
        Assert.Equal(mask, request.FieldMask);
        Sync.BlockRef point = Assert.Single(request.Intersect);
        Assert.Equal(5UL, point.Slot);
        Assert.True(point.Hash.Span.SequenceEqual(hash));
    }

    /// <summary>Leaves intersections empty when following without a starting point.</summary>
    [Fact]
    public async Task FollowTipAsyncWithoutPoint()
    {
        FieldMask mask = FieldMask.FromString("native_bytes");
        await foreach (V1Beta.FollowTipEvent _ in client.V1Beta.Sync.FollowTipAsync(null, mask, Token)) break;
        Sync.FollowTipRequest request = TakeRequest<Sync.FollowTipRequest>();
        Assert.Equal(mask, request.FieldMask);
        Assert.Empty(request.Intersect);
    }

    /// <summary>Uses the typed Cardano state query overload.</summary>
    [Fact]
    public async Task ReadStateAsync()
    {
        FieldMask mask = FieldMask.FromString("native_bytes");
        Query.ReadStateResponse response = await client.V1Beta.Query.ReadStateAsync(new Utxorpc.V1Beta.Cardano.StateQuery(), mask, Token);
        Assert.Equal(9UL, response.LedgerTip.Slot);
        Query.ReadStateRequest request = TakeRequest<Query.ReadStateRequest>();
        Assert.Equal(mask, request.FieldMask);
        Assert.NotNull(request.Query.Cardano);
    }

    /// <summary>Reads the fake genesis configuration with and without a field mask.</summary>
    [Fact]
    public async Task ReadGenesisAsync()
    {
        Query.ReadGenesisResponse response = await client.V1Beta.Query.ReadGenesisAsync(cancellationToken: Token);
        Assert.Equal("fake:1", response.Caip2);
        Assert.Null(TakeRequest<Query.ReadGenesisRequest>().FieldMask);
        FieldMask mask = FieldMask.FromString("genesis");
        await client.V1Beta.Query.ReadGenesisAsync(mask, Token);
        Assert.Equal(mask, TakeRequest<Query.ReadGenesisRequest>().FieldMask);
        Assert.Empty(server.State.Requests);
    }

    /// <summary>Reads the fake Cardano era summary.</summary>
    [Fact]
    public async Task ReadEraSummaryAsync()
    {
        FieldMask mask = FieldMask.FromString("cardano");
        Query.ReadEraSummaryResponse response = await client.V1Beta.Query.ReadEraSummaryAsync(mask, Token);
        Assert.Equal(Query.ReadEraSummaryResponse.SummaryOneofCase.Cardano, response.SummaryCase);
        Assert.Equal(mask, TakeRequest<Query.ReadEraSummaryRequest>().FieldMask);
    }

    /// <summary>Preserves datum hashes and response order.</summary>
    [Fact]
    public async Task ReadDataAsync()
    {
        byte[] hashA = [19, 20];
        byte[] hashB = [21, 22];
        FieldMask mask = FieldMask.FromString("native_bytes");
        Query.ReadDataResponse response = await client.V1Beta.Query.ReadDataAsync([hashA, hashB], mask, Token);
        Query.ReadDataRequest request = TakeRequest<Query.ReadDataRequest>();
        Assert.Equal(mask, request.FieldMask);
        Assert.Equal(2, request.Keys.Count);
        Assert.True(request.Keys[0].Span.SequenceEqual(hashA));
        Assert.True(request.Keys[1].Span.SequenceEqual(hashB));
        Assert.Equal(2, response.Values.Count);
        Assert.Equal(request.Keys[0], response.Values[0].Key);
        Assert.Equal(request.Keys[1], response.Values[1].Key);
    }

    /// <summary>Passes raw bytes into a transaction evaluation and returns its report.</summary>
    [Fact]
    public async Task EvalTxAsync()
    {
        byte[] tx = [23, 24];
        Submit.EvalTxResponse response = await client.V1Beta.Submit.EvalTxAsync(tx, Token);
        Assert.True(TakeRequest<Submit.EvalTxRequest>().Tx.Raw.Span.SequenceEqual(tx));
        Assert.Equal(Submit.AnyChainEval.ChainOneofCase.Cardano, response.Report.ChainCase);
    }

    /// <summary>Reads the fixed mempool entry and its stage.</summary>
    [Fact]
    public async Task ReadMempoolAsync()
    {
        Submit.ReadMempoolResponse response = await client.V1Beta.Submit.ReadMempoolAsync(Token);
        Submit.TxInMempool item = Assert.Single(response.Items);
        Assert.True(item.Ref.Span.SequenceEqual(new byte[] { 9 }));
        Assert.Equal(Submit.Stage.Mempool, item.Stage);
    }

    /// <summary>Preserves a typed mempool predicate and yields changes in order.</summary>
    [Fact]
    public async Task WatchMempoolAsync()
    {
        Submit.TxPredicate predicate = new Submit.TxPredicate { Match = new() { Cardano = new() } };
        FieldMask mask = FieldMask.FromString("tx.ref");
        List<byte> references = new();
        await foreach (Submit.WatchMempoolResponse response in client.V1Beta.Submit.WatchMempoolAsync(predicate, mask, Token))
        {
            references.Add(Assert.Single(response.Tx.Ref));
            if (references.Count == 2) break;
        }
        Assert.Equal(new byte[] { 1, 2 }, references);
        Submit.WatchMempoolRequest request = TakeRequest<Submit.WatchMempoolRequest>();
        Assert.Equal(predicate, request.Predicate);
        Assert.Equal(mask, request.FieldMask);
    }

    /// <summary>Pages through history with exact tokens, supports no start, and rejects zero size immediately.</summary>
    [Fact]
    public async Task DumpAllHistoryAsync()
    {
        byte[] hash = [25, 26];
        FieldMask mask = FieldMask.FromString("native_bytes");
        List<byte> blocks = new();
        await foreach (Sync.AnyChainBlock block in client.V1Beta.Sync.DumpAllHistoryAsync(new BlockPoint(1, hash), pageSize: 2, fieldMask: mask, cancellationToken: Token))
        {
            blocks.Add(Assert.Single(block.NativeBytes));
        }
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, blocks);
        for (int page = 0; page < 3; page++)
        {
            Sync.DumpHistoryRequest request = TakeRequest<Sync.DumpHistoryRequest>();
            Assert.Equal(mask, request.FieldMask);
            Assert.Equal((ulong)(page * 2 + 1), request.StartToken.Slot);
            if (page == 0) Assert.True(request.StartToken.Hash.Span.SequenceEqual(hash));
            else Assert.True(request.StartToken.Hash.IsEmpty);
            Assert.Equal(2U, request.MaxItems);
        }
        Assert.Empty(server.State.Requests);
        await foreach (Sync.AnyChainBlock _ in client.V1Beta.Sync.DumpAllHistoryAsync(null, pageSize: 2, cancellationToken: Token)) break;
        Sync.DumpHistoryRequest first = TakeRequest<Sync.DumpHistoryRequest>();
        Assert.Null(first.StartToken);
        Assert.Null(first.FieldMask);
        Assert.Empty(server.State.Requests);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.V1Beta.Sync.DumpAllHistoryAsync(null, pageSize: 0, cancellationToken: Token));
        Assert.Empty(server.State.Requests);
    }

    /// <summary>Preserves a typed predicate and intersection, or omits both when null.</summary>
    [Fact]
    public async Task WatchTxAsync()
    {
        byte[] hash = [27, 28];
        Watch.TxPredicate predicate = new Watch.TxPredicate { Match = new() { Cardano = new() } };
        FieldMask mask = FieldMask.FromString("apply.block.native_bytes");
        List<byte> blocks = new();
        await foreach (V1Beta.WatchTxEvent response in client.V1Beta.Watch.WatchTxAsync(predicate, new BlockPoint(5, hash), mask, Token))
        {
            Assert.True(response.TryGetValue(out V1Beta.TxApplied applied));
            blocks.Add(Assert.Single(applied.Tx.Block.NativeBytes));
            if (blocks.Count == 2) break;
        }
        Assert.Equal(new byte[] { 1, 2 }, blocks);
        Watch.WatchTxRequest request = TakeRequest<Watch.WatchTxRequest>();
        Assert.Equal(predicate, request.Predicate);
        Assert.Equal(mask, request.FieldMask);
        Watch.BlockRef point = Assert.Single(request.Intersect);
        Assert.Equal(5UL, point.Slot);
        Assert.True(point.Hash.Span.SequenceEqual(hash));
        await foreach (V1Beta.WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: Token)) break;
        request = TakeRequest<Watch.WatchTxRequest>();
        Assert.Null(request.Predicate);
        Assert.Null(request.FieldMask);
        Assert.Empty(request.Intersect);
    }

    /// <summary>Passes a non-null mask to every masked request type.</summary>
    [Fact]
    public async Task FieldMaskReachesEveryRequest()
    {
        FieldMask mask = FieldMask.FromString("native_bytes");
        byte[] hash = [29, 30];
        server.State.BlockBytes = [];
        await client.V1Beta.Query.ReadParamsAsync(mask, Token);
        await client.V1Beta.Query.ReadUtxosAsync([new(hash, 0)], mask, Token);
        await foreach (Query.AnyUtxoData _ in client.V1Beta.Query.SearchAllUtxosAsync(new Query.UtxoPredicate(), fieldMask: mask, cancellationToken: Token)) break;
        await foreach (Query.AnyUtxoData _ in client.V1Beta.Query.SearchUtxosByAddressAsync(hash, fieldMask: mask, cancellationToken: Token)) break;
        await client.V1Beta.Query.ReadTxAsync(hash, mask, Token);
        await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(1, hash), mask, Token);
        await foreach (V1Beta.FollowTipEvent _ in client.V1Beta.Sync.FollowTipAsync(null, mask, Token)) break;
        await client.V1Beta.Query.ReadGenesisAsync(mask, Token);
        await client.V1Beta.Query.ReadEraSummaryAsync(mask, Token);
        await client.V1Beta.Query.ReadDataAsync([hash], mask, Token);
        await foreach (Submit.WatchMempoolResponse _ in client.V1Beta.Submit.WatchMempoolAsync(null, mask, Token)) break;
        await foreach (Sync.AnyChainBlock _ in client.V1Beta.Sync.DumpAllHistoryAsync(null, 2, mask, Token)) break;
        await foreach (V1Beta.WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(null, null, mask, Token)) break;
        await client.V1Beta.Query.ReadStateAsync(new Utxorpc.V1Beta.Cardano.StateQuery(), mask, Token);
        HashSet<System.Type> expected = SpecCoverageTests.MaskedRequestTypes("V1Beta");
        HashSet<System.Type> seen = new();
        foreach (IMessage request in server.State.Requests)
        {
            Assert.Equal(mask, request.Descriptor.FindFieldByName("field_mask").Accessor.GetValue(request));
            seen.Add(request.GetType());
        }
        Assert.True(expected.SetEquals(seen));
        Assert.Equal(14, server.State.Requests.Count);
    }

    /// <summary>Reads each typed case without losing its payload or tip, and ends the call on an unknown action.</summary>
    [Theory]
    [InlineData("FollowTip")]
    [InlineData("WatchTx")]
    public async Task StreamEventsHaveTheirCase(string call)
    {
        server.State.MixedActions = true;
        if (call == "FollowTip")
        {
            await using IAsyncEnumerator<V1Beta.FollowTipEvent> items = client.V1Beta.Sync.FollowTipAsync(null, cancellationToken: Token).GetAsyncEnumerator();
            for (int i = 1; i <= 3; i++)
            {
                Assert.True(await items.MoveNextAsync());
                V1Beta.FollowTipEvent e = items.Current;
                int kind = e switch
                {
                    V1Beta.BlockApplied a => Check(a.Block, e.Response.Apply, Assert.Single(a.Block.NativeBytes), 1),
                    V1Beta.BlockUndone u => Check(u.Block, e.Response.Undo, Assert.Single(u.Block.NativeBytes), 2),
                    V1Beta.Reset r => Check(r.Point, e.Response.Reset, (int)r.Point.Slot, 3)
                };
                Assert.Equal(i, kind);
                Assert.Same(e.Response.Tip, e.Tip);
                Assert.Equal((ulong)(100 + i), e.Tip!.Slot);
            }
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => items.MoveNextAsync().AsTask());
            Assert.Contains(call, error.Message, StringComparison.Ordinal);
        }
        else
        {
            await using IAsyncEnumerator<V1Beta.WatchTxEvent> items = client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: Token).GetAsyncEnumerator();
            for (int i = 1; i <= 3; i++)
            {
                Assert.True(await items.MoveNextAsync());
                V1Beta.WatchTxEvent e = items.Current;
                int kind = e switch
                {
                    V1Beta.TxApplied a => Check(a.Tx, e.Response.Apply, Assert.Single(a.Tx.Block.NativeBytes), 1),
                    V1Beta.TxUndone u => Check(u.Tx, e.Response.Undo, Assert.Single(u.Tx.Block.NativeBytes), 2),
                    V1Beta.Idle idle => Check(idle.Point, e.Response.Idle, (int)idle.Point.Slot, 3)
                };
                Assert.Equal(i, kind);
            }
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => items.MoveNextAsync().AsTask());
            Assert.Contains(call, error.Message, StringComparison.Ordinal);
        }
        await server.State.WaitForStreamEndAsync("v1beta");

        static int Check(object payload, object response, int value, int kind)
        {
            Assert.Same(payload, response);
            Assert.Equal(kind, value);
            return kind;
        }
    }

    /// <summary>Copies several request values in order and reuses them on each stream enumeration.</summary>
    [Fact]
    public async Task SeveralValuesReachTheServerInOrder()
    {
        byte[] h1 = [1], h2 = [2], h3 = [3];
        BlockPoint p1 = new(1, h1), p2 = new(2, h2), p3 = new(3, h3);
        BlockPoint[] points = [p3, p1, p2];
        FieldMask mask = FieldMask.FromString("native_bytes");
        server.State.BlockBytes = [];
        await client.V1Beta.Sync.FetchBlockAsync([p3, p1, p2], mask, Token);
        Sync.FetchBlockRequest fetch = TakeRequest<Sync.FetchBlockRequest>();
        Check(fetch.Ref.Select(point => (point.Slot, point.Hash)));
        Assert.Equal(mask, fetch.FieldMask);
        IAsyncEnumerable<V1Beta.FollowTipEvent> twice = client.V1Beta.Sync.FollowTipAsync([p3, p1, p2], mask, Token);
        for (int i = 0; i < 2; i++)
        {
            await foreach (V1Beta.FollowTipEvent _ in twice) break;
            Sync.FollowTipRequest follow = TakeRequest<Sync.FollowTipRequest>();
            Check(follow.Intersect.Select(point => (point.Slot, point.Hash)));
            Assert.Equal(mask, follow.FieldMask);
        }
        await foreach (V1Beta.WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(null, points, mask, Token)) break;
        Watch.WatchTxRequest watch = TakeRequest<Watch.WatchTxRequest>();
        Check(watch.Intersect.Select(point => (point.Slot, point.Hash)));
        Assert.Equal(mask, watch.FieldMask);
        await foreach (Submit.WaitForTxResponse _ in client.V1Beta.Submit.WaitForTxAsync([h3, h1, h2], Token)) break;
        Assert.Equal(new[] { ByteString.CopyFrom(h3), ByteString.CopyFrom(h1), ByteString.CopyFrom(h2) }, TakeRequest<Submit.WaitForTxRequest>().Ref);
        Assert.Empty(server.State.Requests);
        void Check(IEnumerable<(ulong Slot, ByteString Hash)> actual) => Assert.Equal(
            points.Select(point => (point.Slot, ByteString.CopyFrom(point.Hash.Span))), actual);
    }

    /// <summary>Keeps nullable, default, scalar and argument-free calls bound to their original meaning.</summary>
    [Fact]
    public async Task NullDefaultAndOnePointKeepTheirMeaning()
    {
        BlockPoint point = new(7, new byte[] { 1, 2 });
        BlockPoint? none = null, some = point;
        FieldMask mask = FieldMask.FromString("native_bytes");
        (IAsyncEnumerable<V1Beta.FollowTipEvent> Stream, int Count)[] follows = [
            (client.V1Beta.Sync.FollowTipAsync(null, mask, Token), 0), (client.V1Beta.Sync.FollowTipAsync(default, mask, Token), 0),
            (client.V1Beta.Sync.FollowTipAsync(from: null, cancellationToken: Token), 0), (client.V1Beta.Sync.FollowTipAsync(none, mask, Token), 0),
            (client.V1Beta.Sync.FollowTipAsync([], mask, Token), 0), (client.V1Beta.Sync.FollowTipAsync(some, mask, Token), 1),
            (client.V1Beta.Sync.FollowTipAsync(new BlockPoint(7, point.Hash), mask, Token), 1),
            (client.V1Beta.Sync.FollowTipAsync(), 0), (client.V1Beta.Sync.FollowTipAsync(cancellationToken: Token), 0)];
        foreach (var (stream, count) in follows)
        {
            await foreach (V1Beta.FollowTipEvent _ in stream.WithCancellation(Token)) break;
            Assert.Equal(count, TakeRequest<Sync.FollowTipRequest>().Intersect.Count);
        }
        (IAsyncEnumerable<V1Beta.WatchTxEvent> Stream, int Count)[] watches = [
            (client.V1Beta.Watch.WatchTxAsync(null, null, mask, Token), 0), (client.V1Beta.Watch.WatchTxAsync(null, default, mask, Token), 0),
            (client.V1Beta.Watch.WatchTxAsync(null, point, mask, Token), 1), (client.V1Beta.Watch.WatchTxAsync(), 0)];
        foreach (var (stream, count) in watches)
        {
            await foreach (V1Beta.WatchTxEvent _ in stream.WithCancellation(Token)) break;
            Watch.WatchTxRequest request = TakeRequest<Watch.WatchTxRequest>();
            Assert.Equal(count, request.Intersect.Count);
            Assert.Null(request.Predicate);
        }
        server.State.BlockBytes = [];
        await client.V1Beta.Sync.FetchBlockAsync(default, mask, Token);
        Sync.BlockRef fetched = Assert.Single(TakeRequest<Sync.FetchBlockRequest>().Ref);
        Assert.Equal(0UL, fetched.Slot);
        Assert.True(fetched.Hash.IsEmpty);
        await foreach (Submit.WaitForTxResponse _ in client.V1Beta.Submit.WaitForTxAsync(default, Token)) break;
        Assert.True(Assert.Single(TakeRequest<Submit.WaitForTxRequest>().Ref).IsEmpty);
        byte[] hash = [3, 4];
        await foreach (Submit.WaitForTxResponse _ in client.V1Beta.Submit.WaitForTxAsync(hash, Token)) break;
        Assert.Equal(ByteString.CopyFrom(hash), Assert.Single(TakeRequest<Submit.WaitForTxRequest>().Ref));
        await foreach (Submit.WatchMempoolResponse _ in client.V1Beta.Submit.WatchMempoolAsync().WithCancellation(Token)) break;
        Assert.Null(TakeRequest<Submit.WatchMempoolRequest>().Predicate);
        Assert.Empty(server.State.Requests);
    }

    private T TakeRequest<T>() where T : class, IMessage
    {
        Assert.True(server.State.Requests.TryDequeue(out IMessage? request));
        return Assert.IsType<T>(request);
    }
}
