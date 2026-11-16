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
        Query.ReadParamsResponse response = await client.V1Beta.Query.ReadParamsAsync(Token);
        Assert.Equal(7UL, response.LedgerTip.Slot);
    }

    /// <summary>Preserves output references and their order.</summary>
    [Fact]
    public async Task ReadUtxosAsync()
    {
        byte[] hashA = [1, 2];
        byte[] hashB = [3, 4];
        Query.ReadUtxosResponse response = await client.V1Beta.Query.ReadUtxosAsync([new(hashA, 0), new(hashB, 3)], Token);
        Query.ReadUtxosRequest request = TakeRequest<Query.ReadUtxosRequest>();
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
        foreach (int pageSize in new[] { 0, 2 })
        {
            List<byte> values = new();
            await foreach (Query.AnyUtxoData item in client.V1Beta.Query.SearchAllUtxosAsync(predicate, pageSize, Token))
            {
                values.Add(item.NativeBytes[0]);
            }
            Assert.Equal(new byte[] { 1, 2, 3 }, values);
            for (int page = 0; page < 3; page++)
            {
                Query.SearchUtxosRequest request = TakeRequest<Query.SearchUtxosRequest>();
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
        await foreach (Query.AnyUtxoData _ in client.V1Beta.Query.SearchUtxosByAddressAsync(address, cancellationToken: Token)) break;
        Query.SearchUtxosRequest request = TakeRequest<Query.SearchUtxosRequest>();
        Assert.True(request.Predicate.Match.Cardano.Address.ExactAddress.Span.SequenceEqual(address));
    }

    /// <summary>Propagates cancellation while a subsequent page is pending.</summary>
    [Fact]
    public async Task CancellingWhileSecondPageIsHeldThrowsOperationCanceled()
    {
        server.State.HoldSecondPage = true;
        await using IAsyncEnumerator<Query.AnyUtxoData> items = client.V1Beta.Query
            .SearchAllUtxosAsync(new Query.UtxoPredicate(), cancellationToken: Token).GetAsyncEnumerator();
        Assert.True(await items.MoveNextAsync());
        Task<bool> pendingPage = items.MoveNextAsync().AsTask();
        while (server.State.Requests.Count < 2) await Task.Delay(10, Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingPage);
    }

    /// <summary>Passes transaction hash bytes into the generated request.</summary>
    [Fact]
    public async Task ReadTxAsync()
    {
        byte[] hash = [8, 9, 10];
        Query.ReadTxResponse response = await client.V1Beta.Query.ReadTxAsync(hash, Token);
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
        server.State.BlockBytes = [];
        await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(5, hash), Token);
        await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(5, hash)).WaitAsync(Token);
        await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(5, hash), cancellationToken: Token);
        for (int i = 0; i < 3; i++)
        {
            Sync.BlockRef point = Assert.Single(TakeRequest<Sync.FetchBlockRequest>().Ref);
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
        List<ulong> slots = new();
        await foreach (Sync.FollowTipResponse response in client.V1Beta.Sync.FollowTipAsync(new BlockPoint(5, hash), Token))
        {
            slots.Add(response.Reset.Slot);
            if (slots.Count == 3) break;
        }
        Assert.Equal(new ulong[] { 1, 2, 3 }, slots);
        Sync.BlockRef point = Assert.Single(TakeRequest<Sync.FollowTipRequest>().Intersect);
        Assert.Equal(5UL, point.Slot);
        Assert.True(point.Hash.Span.SequenceEqual(hash));
    }

    /// <summary>Leaves intersections empty when following without a starting point.</summary>
    [Fact]
    public async Task FollowTipAsyncWithoutPoint()
    {
        await foreach (Sync.FollowTipResponse _ in client.V1Beta.Sync.FollowTipAsync(null, Token)) break;
        Assert.Empty(TakeRequest<Sync.FollowTipRequest>().Intersect);
    }

    /// <summary>Uses the typed Cardano state query overload.</summary>
    [Fact]
    public async Task ReadStateAsync()
    {
        Query.ReadStateResponse response = await client.V1Beta.Query.ReadStateAsync(new Utxorpc.V1Beta.Cardano.StateQuery(), Token);
        Assert.Equal(9UL, response.LedgerTip.Slot);
        Assert.NotNull(TakeRequest<Query.ReadStateRequest>().Query.Cardano);
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
        await foreach (Watch.WatchTxResponse response in client.V1Beta.Watch.WatchTxAsync(predicate, new BlockPoint(5, hash), mask, Token))
        {
            Assert.Equal(Watch.WatchTxResponse.ActionOneofCase.Apply, response.ActionCase);
            blocks.Add(Assert.Single(response.Apply.Block.NativeBytes));
            if (blocks.Count == 2) break;
        }
        Assert.Equal(new byte[] { 1, 2 }, blocks);
        Watch.WatchTxRequest request = TakeRequest<Watch.WatchTxRequest>();
        Assert.Equal(predicate, request.Predicate);
        Assert.Equal(mask, request.FieldMask);
        Watch.BlockRef point = Assert.Single(request.Intersect);
        Assert.Equal(5UL, point.Slot);
        Assert.True(point.Hash.Span.SequenceEqual(hash));
        await foreach (Watch.WatchTxResponse _ in client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: Token)) break;
        request = TakeRequest<Watch.WatchTxRequest>();
        Assert.Null(request.Predicate);
        Assert.Null(request.FieldMask);
        Assert.Empty(request.Intersect);
    }

    private T TakeRequest<T>() where T : class, IMessage
    {
        Assert.True(server.State.Requests.TryDequeue(out IMessage? request));
        return Assert.IsType<T>(request);
    }
}
