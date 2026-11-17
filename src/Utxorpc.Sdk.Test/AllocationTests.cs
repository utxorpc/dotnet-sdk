using Google.Protobuf;
using Grpc.Core;
using Xunit.Abstractions;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test;

/// <summary>Runs exact allocation comparisons without concurrent tests.</summary>
[CollectionDefinition("Allocation", DisableParallelization = true)]
public sealed class AllocationCollection;

/// <summary>Checks SDK call overhead using cached, synchronously completed responses.</summary>
[Collection("Allocation")]
[Trait("Category", "Unit")]
public sealed class AllocationTests(ITestOutputHelper output)
{
    /// <summary>Requires reusable client metadata to add no allocation per unary call.</summary>
    [Fact]
    public async Task ClientHeadersAddNoAllocationPerCall()
    {
        StubChannel channel = new();
        using UtxorpcClient withHeaders = new(channel, [new("x-test", "1")]);
        using UtxorpcClient plain = new(channel);
        await withHeaders.V1Beta.Sync.ReadTipAsync(new());
        Metadata? first = channel.Invoker.LastHeaders;
        Assert.NotNull(first);
        Assert.Equal("1", Assert.Single(first).Value);
        await withHeaders.V1Beta.Sync.ReadTipAsync(new());
        Assert.Same(first, channel.Invoker.LastHeaders);
        await plain.V1Beta.Sync.ReadTipAsync(new());
        Assert.Null(channel.Invoker.LastHeaders);
        await Compare("client headers",
            () => withHeaders.V1Beta.Sync.ReadTipAsync(new()).ResponseAsync,
            () => plain.V1Beta.Sync.ReadTipAsync(new()).ResponseAsync);
    }

    /// <summary>Requires the always-on invoker to add no allocation without a deadline.</summary>
    [Fact]
    public async Task CallsWithoutADeadlineAllocateLikeTheBareClient()
    {
        StubChannel channel = new();
        using UtxorpcClient client = new(channel);
        Beta.Sync.SyncService.SyncServiceClient bare = new(channel.Invoker);
        await Compare("raw unary without deadline", () => client.V1Beta.Sync.ReadTipAsync(new Beta.Sync.ReadTipRequest()).ResponseAsync,
            () => bare.ReadTipAsync(new Beta.Sync.ReadTipRequest()).ResponseAsync);
        channel.Invoker.StreamMessages = [new Beta.Sync.FollowTipResponse { Apply = new(), Tip = new() }];
        foreach (int length in new[] { 1, 31 })
        {
            channel.Invoker.StreamLength = length;
            await Compare($"raw stream without deadline, {length} events",
                () => ReadStream(client.V1Beta.Sync.FollowTip(new Beta.Sync.FollowTipRequest()).ReadAllAsync(), static message => (int)message.ActionCase),
                () => ReadStream(bare.FollowTip(new Beta.Sync.FollowTipRequest()).ReadAllAsync(), static message => (int)message.ActionCase));
        }
    }

    /// <summary>Requires completed deadline calls to pass through and buffered stream reads to add no cost.</summary>
    [Fact]
    public async Task DeadlineCallsCostAsDocumented()
    {
        StubChannel channel = new();
        using UtxorpcClient client = new(channel);
        Beta.Sync.SyncService.SyncServiceClient bare = new(channel.Invoker);
        DateTime far = DateTime.UtcNow.AddHours(1);
        await Compare("completed deadline unary", () => client.V1Beta.Sync.ReadTipAsync(new Beta.Sync.ReadTipRequest(), deadline: far).ResponseAsync,
            () => bare.ReadTipAsync(new Beta.Sync.ReadTipRequest(), deadline: far).ResponseAsync);
        channel.Invoker.StreamMessages = [new Beta.Sync.FollowTipResponse { Apply = new(), Tip = new() }];
        foreach (int length in new[] { 1, 31 })
        {
            channel.Invoker.StreamLength = length;
            long sdk = await Measure(() => ReadStream(client.V1Beta.Sync.FollowTip(new Beta.Sync.FollowTipRequest(), deadline: far).ReadAllAsync(), static message => (int)message.ActionCase));
            long raw = await Measure(() => ReadStream(bare.FollowTip(new Beta.Sync.FollowTipRequest(), deadline: far).ReadAllAsync(), static message => (int)message.ActionCase));
            output.WriteLine($"deadline raw stream, {length} buffered events: SDK={sdk}, bare={raw} B per 1000 streams.");
            Assert.Equal(104000, sdk - raw);
        }
    }

    /// <summary>Requires every unary short method to allocate exactly its raw request's cost.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShortMethodsAllocateLikeTheRawCall(bool beta)
    {
        using UtxorpcClient client = new(new StubChannel());
        ReadOnlyMemory<byte> hash = new byte[32];
        ReadOnlyMemory<byte> tx = new byte[64];
        ReadOnlyMemory<byte>[] hashes = [hash, hash];
        OutputReference[] references = [new(hash, 0), new(hash, 1)];
        BlockPoint point = new(42, hash);
        if (!beta)
        {
            V1AlphaClients a = client.V1Alpha;
            await Compare("v1alpha.ReadTip", () => a.Sync.ReadTipAsync(),
                () => a.Sync.ReadTipAsync(new Alpha.Sync.ReadTipRequest()).ResponseAsync);
            await Compare("v1alpha.ReadParams", () => a.Query.ReadParamsAsync(),
                () => a.Query.ReadParamsAsync(new Alpha.Query.ReadParamsRequest()).ResponseAsync);
            await Compare("v1alpha.ReadGenesis", () => a.Query.ReadGenesisAsync(),
                () => a.Query.ReadGenesisAsync(new Alpha.Query.ReadGenesisRequest()).ResponseAsync);
            await Compare("v1alpha.ReadEraSummary", () => a.Query.ReadEraSummaryAsync(),
                () => a.Query.ReadEraSummaryAsync(new Alpha.Query.ReadEraSummaryRequest()).ResponseAsync);
            await Compare("v1alpha.ReadTx", () => a.Query.ReadTxAsync(hash),
                () => a.Query.ReadTxAsync(new Alpha.Query.ReadTxRequest { Hash = UnsafeByteOperations.UnsafeWrap(hash) }).ResponseAsync);
            await Compare("v1alpha.EvalTx", () => a.Submit.EvalTxAsync(tx),
                () => a.Submit.EvalTxAsync(new Alpha.Submit.EvalTxRequest { Tx = new() { Raw = UnsafeByteOperations.UnsafeWrap(tx) } }).ResponseAsync);
            await Compare("v1alpha.SubmitTx", () => a.Submit.SubmitTxAsync(tx),
                () => a.Submit.SubmitTxAsync(new Alpha.Submit.SubmitTxRequest { Tx = new() { Raw = UnsafeByteOperations.UnsafeWrap(tx) } }).ResponseAsync);
            await Compare("v1alpha.ReadMempool", () => a.Submit.ReadMempoolAsync(),
                () => a.Submit.ReadMempoolAsync(new Alpha.Submit.ReadMempoolRequest()).ResponseAsync);
            await Compare("v1alpha.ReadData", () => a.Query.ReadDataAsync(hashes), () =>
            {
                Alpha.Query.ReadDataRequest request = new();
                request.Keys.Capacity = 2;
                request.Keys.Add(UnsafeByteOperations.UnsafeWrap(hashes[0]));
                request.Keys.Add(UnsafeByteOperations.UnsafeWrap(hashes[1]));
                return a.Query.ReadDataAsync(request).ResponseAsync;
            });
            await Compare("v1alpha.ReadUtxos", () => a.Query.ReadUtxosAsync(references), () =>
            {
                Alpha.Query.ReadUtxosRequest request = new();
                request.Keys.Capacity = 2;
                request.Keys.Add(new Alpha.Query.TxoRef { Hash = UnsafeByteOperations.UnsafeWrap(references[0].TxHash), Index = references[0].Index });
                request.Keys.Add(new Alpha.Query.TxoRef { Hash = UnsafeByteOperations.UnsafeWrap(references[1].TxHash), Index = references[1].Index });
                return a.Query.ReadUtxosAsync(request).ResponseAsync;
            });
            await Compare("v1alpha.FetchBlock", () => a.Sync.FetchBlockAsync(point), () =>
            {
                Alpha.Sync.FetchBlockRequest request = new();
                request.Ref.Capacity = 1;
                request.Ref.Add(new Alpha.Sync.BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) });
                return a.Sync.FetchBlockAsync(request).ResponseAsync;
            });
            BlockPoint[] points = [point, point, point];
            await Compare("v1alpha.FetchBlock(3 points)", () => a.Sync.FetchBlockAsync(points), () =>
            {
                Alpha.Sync.FetchBlockRequest request = new();
                request.Ref.Capacity = points.Length;
                foreach (BlockPoint value in points) request.Ref.Add(new Alpha.Sync.BlockRef { Slot = value.Slot, Hash = UnsafeByteOperations.UnsafeWrap(value.Hash) });
                return a.Sync.FetchBlockAsync(request).ResponseAsync;
            });
        }
        else
        {
            V1BetaClients b = client.V1Beta;
            Beta.Cardano.StateQuery state = new() { StakePoolDistribution = new() };
            await Compare("v1beta.ReadTip", () => b.Sync.ReadTipAsync(),
                () => b.Sync.ReadTipAsync(new Beta.Sync.ReadTipRequest()).ResponseAsync);
            await Compare("v1beta.ReadParams", () => b.Query.ReadParamsAsync(),
                () => b.Query.ReadParamsAsync(new Beta.Query.ReadParamsRequest()).ResponseAsync);
            await Compare("v1beta.ReadGenesis", () => b.Query.ReadGenesisAsync(),
                () => b.Query.ReadGenesisAsync(new Beta.Query.ReadGenesisRequest()).ResponseAsync);
            await Compare("v1beta.ReadEraSummary", () => b.Query.ReadEraSummaryAsync(),
                () => b.Query.ReadEraSummaryAsync(new Beta.Query.ReadEraSummaryRequest()).ResponseAsync);
            await Compare("v1beta.ReadTx", () => b.Query.ReadTxAsync(hash),
                () => b.Query.ReadTxAsync(new Beta.Query.ReadTxRequest { Hash = UnsafeByteOperations.UnsafeWrap(hash) }).ResponseAsync);
            await Compare("v1beta.EvalTx", () => b.Submit.EvalTxAsync(tx),
                () => b.Submit.EvalTxAsync(new Beta.Submit.EvalTxRequest { Tx = new() { Raw = UnsafeByteOperations.UnsafeWrap(tx) } }).ResponseAsync);
            await Compare("v1beta.SubmitTx", () => b.Submit.SubmitTxAsync(tx),
                () => b.Submit.SubmitTxAsync(new Beta.Submit.SubmitTxRequest { Tx = new() { Raw = UnsafeByteOperations.UnsafeWrap(tx) } }).ResponseAsync);
            await Compare("v1beta.ReadMempool", () => b.Submit.ReadMempoolAsync(),
                () => b.Submit.ReadMempoolAsync(new Beta.Submit.ReadMempoolRequest()).ResponseAsync);
            await Compare("v1beta.ReadState", () => b.Query.ReadStateAsync(state),
                () => b.Query.ReadStateAsync(new Beta.Query.ReadStateRequest { Query = new() { Cardano = state } }).ResponseAsync);
            await Compare("v1beta.ReadData", () => b.Query.ReadDataAsync(hashes), () =>
            {
                Beta.Query.ReadDataRequest request = new();
                request.Keys.Capacity = 2;
                request.Keys.Add(UnsafeByteOperations.UnsafeWrap(hashes[0]));
                request.Keys.Add(UnsafeByteOperations.UnsafeWrap(hashes[1]));
                return b.Query.ReadDataAsync(request).ResponseAsync;
            });
            await Compare("v1beta.ReadUtxos", () => b.Query.ReadUtxosAsync(references), () =>
            {
                Beta.Query.ReadUtxosRequest request = new();
                request.Keys.Capacity = 2;
                request.Keys.Add(new Beta.Query.TxoRef { Hash = UnsafeByteOperations.UnsafeWrap(references[0].TxHash), Index = references[0].Index });
                request.Keys.Add(new Beta.Query.TxoRef { Hash = UnsafeByteOperations.UnsafeWrap(references[1].TxHash), Index = references[1].Index });
                return b.Query.ReadUtxosAsync(request).ResponseAsync;
            });
            await Compare("v1beta.FetchBlock", () => b.Sync.FetchBlockAsync(point), () =>
            {
                Beta.Sync.FetchBlockRequest request = new();
                request.Ref.Capacity = 1;
                request.Ref.Add(new Beta.Sync.BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) });
                return b.Sync.FetchBlockAsync(request).ResponseAsync;
            });
            BlockPoint[] points = [point, point, point];
            await Compare("v1beta.FetchBlock(3 points)", () => b.Sync.FetchBlockAsync(points), () =>
            {
                Beta.Sync.FetchBlockRequest request = new();
                request.Ref.Capacity = points.Length;
                foreach (BlockPoint value in points) request.Ref.Add(new Beta.Sync.BlockRef { Slot = value.Slot, Hash = UnsafeByteOperations.UnsafeWrap(value.Hash) });
                return b.Sync.FetchBlockAsync(request).ResponseAsync;
            });
        }
    }

    /// <summary>Requires zero extra bytes per event and exactly two saved references per deferred stream.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StreamShortMethodsAllocateLikeTheRawStream(bool beta) => CheckStreams(beta, allocations: true);

    /// <summary>Requires each stream to stay unopened until enumerated and to open and dispose its own call each time.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StreamShortMethodsOpenACallPerEnumeration(bool beta) => CheckStreams(beta, allocations: false);

    private async Task CheckStreams(bool beta, bool allocations)
    {
        StubChannel channel = new();
        using UtxorpcClient client = new(channel);
        ReadOnlyMemory<byte> hash = new byte[32];
        BlockPoint point = new(42, hash);
        BlockPoint[] points = [point, point, point];
        if (!beta)
        {
            V1AlphaClients a = client.V1Alpha;
            await CheckStream("v1alpha.FollowTip", channel.Invoker,
                [new Alpha.Sync.FollowTipResponse { Apply = new(), Tip = new() }, new Alpha.Sync.FollowTipResponse { Undo = new(), Tip = new() }, new Alpha.Sync.FollowTipResponse { Reset = new(), Tip = new() }],
                () => a.Sync.FollowTipAsync(null), () => a.Sync.FollowTip(new Alpha.Sync.FollowTipRequest()),
                static e => e switch { V1Alpha.BlockApplied a => a.Block.NativeBytes.Length + 1, V1Alpha.BlockUndone u => u.Block.NativeBytes.Length + 1, V1Alpha.Reset r => (int)r.Point.Slot + 1 },
                static message => (int)message.ActionCase, allocations);
            await CheckStream("v1alpha.WatchTx", channel.Invoker,
                [new Alpha.Watch.WatchTxResponse { Apply = new() { Block = new() } }, new Alpha.Watch.WatchTxResponse { Undo = new() { Block = new() } }, new Alpha.Watch.WatchTxResponse { Idle = new() }],
                () => a.Watch.WatchTxAsync(null, null), () => a.Watch.WatchTx(new Alpha.Watch.WatchTxRequest()),
                static e => e switch { V1Alpha.TxApplied a => a.Tx.Block.NativeBytes.Length + 1, V1Alpha.TxUndone u => u.Tx.Block.NativeBytes.Length + 1, V1Alpha.Idle i => (int)i.Point.Slot + 1 },
                static message => (int)message.ActionCase, allocations);
            await CheckStream("v1alpha.FollowTip(one point)", channel.Invoker,
                [new Alpha.Sync.FollowTipResponse { Apply = new(), Tip = new() }, new Alpha.Sync.FollowTipResponse { Undo = new(), Tip = new() }, new Alpha.Sync.FollowTipResponse { Reset = new(), Tip = new() }],
                () => a.Sync.FollowTipAsync(point), () =>
                {
                    Alpha.Sync.FollowTipRequest request = new();
                    request.Intersect.Capacity = 1;
                    request.Intersect.Add(new Alpha.Sync.BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) });
                    return a.Sync.FollowTip(request);
                }, static e => e switch { V1Alpha.BlockApplied a => a.Block.NativeBytes.Length + 1, V1Alpha.BlockUndone u => u.Block.NativeBytes.Length + 1, V1Alpha.Reset r => (int)r.Point.Slot + 1 },
                static message => (int)message.ActionCase, allocations);
            await CheckStream("v1alpha.WatchTx(3 points)", channel.Invoker,
                [new Alpha.Watch.WatchTxResponse { Apply = new() { Block = new() } }, new Alpha.Watch.WatchTxResponse { Undo = new() { Block = new() } }, new Alpha.Watch.WatchTxResponse { Idle = new() }],
                () => a.Watch.WatchTxAsync(null, points), () =>
                {
                    Alpha.Watch.WatchTxRequest request = new();
                    request.Intersect.Capacity = points.Length;
                    foreach (BlockPoint value in points) request.Intersect.Add(new Alpha.Watch.BlockRef { Slot = value.Slot, Hash = UnsafeByteOperations.UnsafeWrap(value.Hash) });
                    return a.Watch.WatchTx(request);
                }, static e => e switch { V1Alpha.TxApplied a => a.Tx.Block.NativeBytes.Length + 1, V1Alpha.TxUndone u => u.Tx.Block.NativeBytes.Length + 1, V1Alpha.Idle i => (int)i.Point.Slot + 1 },
                static message => (int)message.ActionCase, allocations);
            await CheckStream("v1alpha.WaitForTx", channel.Invoker,
                [new Alpha.Submit.WaitForTxResponse(), new Alpha.Submit.WaitForTxResponse(), new Alpha.Submit.WaitForTxResponse()],
                () => a.Submit.WaitForTxAsync(hash), () =>
                {
                    Alpha.Submit.WaitForTxRequest request = new();
                    request.Ref.Capacity = 1;
                    request.Ref.Add(UnsafeByteOperations.UnsafeWrap(hash));
                    return a.Submit.WaitForTx(request);
                }, static message => (int)message.Stage + 1, static message => (int)message.Stage + 1, allocations);
            await CheckStream("v1alpha.WatchMempool", channel.Invoker,
                [new Alpha.Submit.WatchMempoolResponse { Tx = new() }, new Alpha.Submit.WatchMempoolResponse { Tx = new() }, new Alpha.Submit.WatchMempoolResponse { Tx = new() }],
                () => a.Submit.WatchMempoolAsync(null), () => a.Submit.WatchMempool(new Alpha.Submit.WatchMempoolRequest()),
                static message => message.Tx.NativeBytes.Length + 1, static message => message.Tx.NativeBytes.Length + 1, allocations);
        }
        else
        {
            V1BetaClients b = client.V1Beta;
            await CheckStream("v1beta.FollowTip", channel.Invoker,
                [new Beta.Sync.FollowTipResponse { Apply = new(), Tip = new() }, new Beta.Sync.FollowTipResponse { Undo = new(), Tip = new() }, new Beta.Sync.FollowTipResponse { Reset = new(), Tip = new() }],
                () => b.Sync.FollowTipAsync(null), () => b.Sync.FollowTip(new Beta.Sync.FollowTipRequest()),
                static e => e switch { V1Beta.BlockApplied a => a.Block.NativeBytes.Length + 1, V1Beta.BlockUndone u => u.Block.NativeBytes.Length + 1, V1Beta.Reset r => (int)r.Point.Slot + 1 },
                static message => (int)message.ActionCase, allocations);
            await CheckStream("v1beta.WatchTx", channel.Invoker,
                [new Beta.Watch.WatchTxResponse { Apply = new() { Block = new() } }, new Beta.Watch.WatchTxResponse { Undo = new() { Block = new() } }, new Beta.Watch.WatchTxResponse { Idle = new() }],
                () => b.Watch.WatchTxAsync(null, null), () => b.Watch.WatchTx(new Beta.Watch.WatchTxRequest()),
                static e => e switch { V1Beta.TxApplied a => a.Tx.Block.NativeBytes.Length + 1, V1Beta.TxUndone u => u.Tx.Block.NativeBytes.Length + 1, V1Beta.Idle i => (int)i.Point.Slot + 1 },
                static message => (int)message.ActionCase, allocations);
            await CheckStream("v1beta.FollowTip(one point)", channel.Invoker,
                [new Beta.Sync.FollowTipResponse { Apply = new(), Tip = new() }, new Beta.Sync.FollowTipResponse { Undo = new(), Tip = new() }, new Beta.Sync.FollowTipResponse { Reset = new(), Tip = new() }],
                () => b.Sync.FollowTipAsync(point), () =>
                {
                    Beta.Sync.FollowTipRequest request = new();
                    request.Intersect.Capacity = 1;
                    request.Intersect.Add(new Beta.Sync.BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) });
                    return b.Sync.FollowTip(request);
                }, static e => e switch { V1Beta.BlockApplied a => a.Block.NativeBytes.Length + 1, V1Beta.BlockUndone u => u.Block.NativeBytes.Length + 1, V1Beta.Reset r => (int)r.Point.Slot + 1 },
                static message => (int)message.ActionCase, allocations);
            await CheckStream("v1beta.WatchTx(3 points)", channel.Invoker,
                [new Beta.Watch.WatchTxResponse { Apply = new() { Block = new() } }, new Beta.Watch.WatchTxResponse { Undo = new() { Block = new() } }, new Beta.Watch.WatchTxResponse { Idle = new() }],
                () => b.Watch.WatchTxAsync(null, points), () =>
                {
                    Beta.Watch.WatchTxRequest request = new();
                    request.Intersect.Capacity = points.Length;
                    foreach (BlockPoint value in points) request.Intersect.Add(new Beta.Watch.BlockRef { Slot = value.Slot, Hash = UnsafeByteOperations.UnsafeWrap(value.Hash) });
                    return b.Watch.WatchTx(request);
                }, static e => e switch { V1Beta.TxApplied a => a.Tx.Block.NativeBytes.Length + 1, V1Beta.TxUndone u => u.Tx.Block.NativeBytes.Length + 1, V1Beta.Idle i => (int)i.Point.Slot + 1 },
                static message => (int)message.ActionCase, allocations);
            await CheckStream("v1beta.WaitForTx", channel.Invoker,
                [new Beta.Submit.WaitForTxResponse(), new Beta.Submit.WaitForTxResponse(), new Beta.Submit.WaitForTxResponse()],
                () => b.Submit.WaitForTxAsync(hash), () =>
                {
                    Beta.Submit.WaitForTxRequest request = new();
                    request.Ref.Capacity = 1;
                    request.Ref.Add(UnsafeByteOperations.UnsafeWrap(hash));
                    return b.Submit.WaitForTx(request);
                }, static message => (int)message.Stage + 1, static message => (int)message.Stage + 1, allocations);
            await CheckStream("v1beta.WatchMempool", channel.Invoker,
                [new Beta.Submit.WatchMempoolResponse { Tx = new() }, new Beta.Submit.WatchMempoolResponse { Tx = new() }, new Beta.Submit.WatchMempoolResponse { Tx = new() }],
                () => b.Submit.WatchMempoolAsync(null), () => b.Submit.WatchMempool(new Beta.Submit.WatchMempoolRequest()),
                static message => message.Tx.NativeBytes.Length + 1, static message => message.Tx.NativeBytes.Length + 1, allocations);
        }
    }

    private async Task CheckStream<TMessage, TResult>(string name, StubInvoker invoker, TMessage[] messages,
        Func<IAsyncEnumerable<TResult>> shortStream, Func<AsyncServerStreamingCall<TMessage>> rawCall,
        Func<TResult, int> readEvent, Func<TMessage, int> readMessage, bool allocations) where TMessage : class
    {
        invoker.StreamMessages = messages;
        if (!allocations)
        {
            invoker.StreamLength = 3;
            int calls = invoker.StreamCalls, disposals = invoker.StreamDisposals;
            IAsyncEnumerable<TResult> stream = shortStream();
            Assert.Equal(calls, invoker.StreamCalls);
            Assert.Equal(3, await ReadStream(stream, readEvent));
            Assert.Equal(3, await ReadStream(stream, readEvent));
            Assert.Equal(calls + 2, invoker.StreamCalls);
            Assert.Equal(disposals + 2, invoker.StreamDisposals);
            return;
        }
        invoker.StreamLength = 1;
        long short1 = await Measure(() => ReadStream(shortStream(), readEvent));
        long raw1 = await Measure(() => ReadStream(rawCall().ReadAllAsync(), readMessage));
        invoker.StreamLength = 31;
        long short31 = await Measure(() => ReadStream(shortStream(), readEvent));
        long raw31 = await Measure(() => ReadStream(rawCall().ReadAllAsync(), readMessage));
        output.WriteLine($"{name}: short1={short1}, raw1={raw1}, short31={short31}, raw31={raw31} B per 1,000 streams.");
        Assert.Equal(raw31 - raw1, short31 - short1);
        Assert.Equal(1000 * 2 * IntPtr.Size, short1 - raw1);
    }

    private static async Task<int> ReadStream<T>(IAsyncEnumerable<T> stream, Func<T, int> read)
    {
        int count = 0, sum = 0;
        await foreach (T item in stream) { sum += read(item); count++; }
        Assert.True(sum > 0);
        return count;
    }

    private async Task Compare(string name, Func<Task> shortCall, Func<Task> rawCall)
    {
        long shortBytes = await Measure(shortCall);
        long rawBytes = await Measure(rawCall);
        output.WriteLine($"{name}: short={shortBytes}, raw={rawBytes} bytes per 1,000 calls.");
        Assert.True(shortBytes == rawBytes, $"{name}: short={shortBytes}, raw={rawBytes} bytes per 1,000 calls.");
    }

    private static async Task<long> Measure(Func<Task> call)
    {
        for (int i = 0; i < 100; i++) await call();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) await call();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    internal sealed class StubChannel() : ChannelBase("allocation-test")
    {
        internal StubInvoker Invoker { get; } = new();
        /// <summary>Returns the caller-owned cached-response invoker.</summary>
        public override CallInvoker CreateCallInvoker() => Invoker;
    }

    internal sealed class StubInvoker : CallInvoker
    {
        internal Metadata? LastHeaders;
        internal bool Cancelled;
        internal object[] StreamMessages = [];
        internal int StreamLength = 3, StreamCalls, StreamDisposals;
        private static readonly Task<Metadata> Headers = Task.FromResult(Metadata.Empty);
        /// <summary>Records headers and returns the cached completed response.</summary>
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            LastHeaders = options.Headers;
            return Cancelled ? new(Task.FromCanceled<TResponse>(new CancellationToken(true)), Headers,
                static () => Status.DefaultSuccess, static () => Metadata.Empty, static () => { }) : Answer<TResponse>.Call;
        }

        /// <summary>Rejects blocking calls, which these measurements do not use.</summary>
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
            string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        /// <summary>Returns a synchronous stream over the configured messages and counts calls and disposals.</summary>
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            StreamCalls++;
            return new(new StubReader<TResponse>(StreamMessages, Cancelled ? -1 : StreamLength), static _ => Headers,
                static _ => Status.DefaultSuccess, static _ => Metadata.Empty, static state => ((StubInvoker)state).StreamDisposals++, this);
        }
        /// <summary>Rejects client streams, which these unary measurements do not use.</summary>
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        /// <summary>Rejects duplex streams, which these unary measurements do not use.</summary>
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    private sealed class StubReader<T>(object[] messages, int length) : IAsyncStreamReader<T>
    {
        private int index;
        /// <summary>Gets the message at the current position.</summary>
        public T Current => (T)messages[(index - 1) % messages.Length];
        /// <summary>Advances synchronously through the repeated messages with cached completed tasks.</summary>
        public Task<bool> MoveNext(CancellationToken cancellationToken) => length < 0
            ? Task.FromCanceled<bool>(new CancellationToken(true)) : Task.FromResult(++index <= length);
    }

    private static class Answer<T> where T : class
    {
        internal static readonly AsyncUnaryCall<T> Call = new(Task.FromResult(Activator.CreateInstance<T>()!),
            Task.FromResult(Metadata.Empty), static () => Status.DefaultSuccess, static () => Metadata.Empty, static () => { });
    }
}
