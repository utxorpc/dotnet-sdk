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
        }
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

    private sealed class StubChannel() : ChannelBase("allocation-test")
    {
        internal StubInvoker Invoker { get; } = new();
        /// <summary>Returns the caller-owned cached-response invoker.</summary>
        public override CallInvoker CreateCallInvoker() => Invoker;
    }

    private sealed class StubInvoker : CallInvoker
    {
        internal Metadata? LastHeaders;
        /// <summary>Records headers and returns the cached completed response.</summary>
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            LastHeaders = options.Headers;
            return Answer<TResponse>.Call;
        }

        /// <summary>Rejects blocking calls, which these measurements do not use.</summary>
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
            string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        /// <summary>Rejects server streams, which these unary measurements do not use.</summary>
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        /// <summary>Rejects client streams, which these unary measurements do not use.</summary>
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        /// <summary>Rejects duplex streams, which these unary measurements do not use.</summary>
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    private static class Answer<T> where T : class
    {
        internal static readonly AsyncUnaryCall<T> Call = new(Task.FromResult(Activator.CreateInstance<T>()!),
            Task.FromResult(Metadata.Empty), static () => Status.DefaultSuccess, static () => Metadata.Empty, static () => { });
    }
}
