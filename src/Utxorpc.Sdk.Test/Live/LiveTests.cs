using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Xunit.Abstractions;
using Xunit.Sdk;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test.Live;

/// <summary>Exercises both protocol versions against a live, unfunded endpoint.</summary>
[Trait("Category", "Live")]
public sealed class LiveTests(LiveServer server, ITestOutputHelper output) : IClassFixture<LiveServer>
{
    private UtxorpcClient Client => server.Client;
    private static CancellationTokenSource Timeout() => new(TimeSpan.FromSeconds(180));

    /// <summary>Reads each version's current tip without assuming the tip stays still.</summary>
    [Fact]
    public async Task ReadTipAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        Alpha.Sync.BlockRef alpha = (await Client.V1Alpha.Sync.ReadTipAsync(timeout.Token)).Tip;
        Beta.Sync.BlockRef beta = (await Client.V1Beta.Sync.ReadTipAsync(timeout.Token)).Tip;
        Assert.Equal(32, alpha.Hash.Length);
        Assert.True(alpha.Slot > 0);
        Assert.Equal(32, beta.Hash.Length);
        Assert.True(beta.Slot > 0);
    }

    /// <summary>Fetches identical native bytes and checks the server's native-only mask.</summary>
    [Fact]
    public async Task FetchBlockAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        CancellationToken ct = timeout.Token;
        Beta.Sync.BlockRef tip = (await Client.V1Beta.Sync.ReadTipAsync(ct)).Tip;
        BlockPoint point = new(tip.Slot, tip.Hash.Memory);
        Alpha.Sync.AnyChainBlock alpha = Assert.Single((await Client.V1Alpha.Sync.FetchBlockAsync(point, cancellationToken: ct)).Block);
        Beta.Sync.AnyChainBlock beta = Assert.Single((await Client.V1Beta.Sync.FetchBlockAsync(point, cancellationToken: ct)).Block);
        Assert.False(alpha.NativeBytes.IsEmpty);
        Assert.Equal(alpha.NativeBytes, beta.NativeBytes);
        FieldMask mask = FieldMask.FromString("native_bytes");
        alpha = Assert.Single((await Client.V1Alpha.Sync.FetchBlockAsync(point, mask, ct)).Block);
        beta = Assert.Single((await Client.V1Beta.Sync.FetchBlockAsync(point, mask, ct)).Block);
        CheckMask(alpha.NativeBytes, alpha.Cardano);
        CheckMask(beta.NativeBytes, beta.Cardano);
        void CheckMask(ByteString bytes, IMessage? parsed)
        {
            Assert.False(bytes.IsEmpty);
            Assert.Null(parsed);
        }
    }

    /// <summary>Compares two raw history pages with both versions' all-pages short method.</summary>
    [Fact]
    public async Task DumpAllHistoryAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        CancellationToken ct = timeout.Token;
        Beta.Sync.BlockRef start = await FindStartAsync();
        Beta.Sync.DumpHistoryResponse first = await Client.V1Beta.Sync.DumpHistoryAsync(new() { StartToken = start, MaxItems = 2 }, cancellationToken: ct);
        Alpha.Sync.DumpHistoryResponse alphaFirst = await Client.V1Alpha.Sync.DumpHistoryAsync(new()
        { StartToken = Alpha.Sync.BlockRef.Parser.ParseFrom(start.ToByteString()), MaxItems = 2 }, cancellationToken: ct);
        Assert.Equal(2, first.Block.Count);
        Assert.Equal(first.Block.Select(block => block.NativeBytes), alphaFirst.Block.Select(block => block.NativeBytes));
        Assert.Equal(first.NextToken.ToByteString(), alphaFirst.NextToken.ToByteString());
        Assert.NotNull(first.NextToken);
        Beta.Sync.DumpHistoryResponse second = await Client.V1Beta.Sync.DumpHistoryAsync(new() { StartToken = first.NextToken, MaxItems = 2 }, cancellationToken: ct);
        Alpha.Sync.DumpHistoryResponse alphaSecond = await Client.V1Alpha.Sync.DumpHistoryAsync(new() { StartToken = alphaFirst.NextToken, MaxItems = 2 }, cancellationToken: ct);
        Assert.Equal(2, second.Block.Count);
        Assert.Equal(second.Block.Select(block => block.NativeBytes), alphaSecond.Block.Select(block => block.NativeBytes));
        Assert.Equal(second.NextToken.ToByteString(), alphaSecond.NextToken.ToByteString());
        Assert.NotNull(second.NextToken);
        Assert.True(second.NextToken.Slot > first.NextToken.Slot);
        Assert.DoesNotContain(second.Block, block => first.Block.Any(previous => previous.Cardano.Header.Hash == block.Cardano.Header.Hash));
        ByteString[] expected = first.Block.Concat(second.Block).Select(block => block.NativeBytes).ToArray();
        BlockPoint point = new(start.Slot, start.Hash.Memory);
        Assert.Equal(expected, (await TakeAsync(Client.V1Alpha.Sync.DumpAllHistoryAsync(point, 2, cancellationToken: ct), 4)).Select(block => block.NativeBytes));
        Assert.Equal(expected, (await TakeAsync(Client.V1Beta.Sync.DumpAllHistoryAsync(point, 2, cancellationToken: ct), 4)).Select(block => block.NativeBytes));
        async Task<Beta.Sync.BlockRef> FindStartAsync()
        {
            Beta.Sync.BlockRef tip = (await Client.V1Beta.Sync.ReadTipAsync(ct)).Tip;
            Beta.Sync.DumpHistoryResponse page;
            try
            {
                // Not the oldest retained block: Dolos prunes that edge while tests run.
                page = await Client.V1Beta.Sync.DumpHistoryAsync(new() { StartToken = new() { Slot = tip.Slot - 3600 }, MaxItems = 1 }, cancellationToken: ct);
            }
            catch (RpcException error)
            {
                throw new XunitException($"DumpHistory refused a slot-only start ({error.StatusCode}).");
            }
            Beta.Cardano.BlockHeader header = Assert.Single(page.Block).Cardano.Header;
            return new() { Slot = header.Slot, Hash = header.Hash };
        }
    }

    /// <summary>Reads two tip events and leaves each stream cleanly, without claiming to test pings.</summary>
    [Fact]
    public async Task FollowTipAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        List<Alpha.Sync.FollowTipResponse> alpha = await TakeAsync(Client.V1Alpha.Sync.FollowTipAsync(null, cancellationToken: timeout.Token), 2);
        List<Beta.Sync.FollowTipResponse> beta = await TakeAsync(Client.V1Beta.Sync.FollowTipAsync(null, cancellationToken: timeout.Token), 2);
        Assert.Equal(2, alpha.Count);
        Assert.Equal(2, beta.Count);
        Assert.All(alpha, item => Assert.NotEqual(Alpha.Sync.FollowTipResponse.ActionOneofCase.None, item.ActionCase));
        Assert.All(beta, item => Assert.NotEqual(Beta.Sync.FollowTipResponse.ActionOneofCase.None, item.ActionCase));
    }

    /// <summary>Compares parameters, genesis and eras without comparing moving ledger tips.</summary>
    [Fact]
    public async Task ReadChainConfigurationAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        CancellationToken ct = timeout.Token;
        Alpha.Query.ReadParamsResponse alpha = await Client.V1Alpha.Query.ReadParamsAsync(cancellationToken: ct);
        Beta.Query.ReadParamsResponse beta = await Client.V1Beta.Query.ReadParamsAsync(cancellationToken: ct);
        // Dolos 1.6.3 has stale preprod parameters and omits pool_retirement_epoch_bound.
        Assert.Equal(ConfigurationBytes(alpha.Values), ConfigurationBytes(beta.Values));
        Assert.Equal(ConfigurationBytes(await Client.V1Alpha.Query.ReadGenesisAsync(cancellationToken: ct)),
            ConfigurationBytes(await Client.V1Beta.Query.ReadGenesisAsync(cancellationToken: ct)));
        Assert.Equal(ConfigurationBytes(await Client.V1Alpha.Query.ReadEraSummaryAsync(cancellationToken: ct)),
            ConfigurationBytes(await Client.V1Beta.Query.ReadEraSummaryAsync(cancellationToken: ct)));
    }

    /// <summary>Observes the first applied transaction from each version independently.</summary>
    [Fact]
    public async Task WatchTxAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        bool alphaFound = false, betaFound = false;
        await foreach (Alpha.Watch.WatchTxResponse item in Client.V1Alpha.Watch.WatchTxAsync(null, null, cancellationToken: timeout.Token))
        {
            if (item.Apply is null) continue;
            Assert.Equal(32, item.Apply.Cardano.Hash.Length);
            alphaFound = true;
            break;
        }
        await foreach (Beta.Watch.WatchTxResponse item in Client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: timeout.Token))
        {
            if (item.Apply is null) continue;
            Assert.Equal(32, item.Apply.Cardano.Hash.Length);
            betaFound = true;
            break;
        }
        Assert.True(alphaFound && betaFound);
    }

    /// <summary>Reads a discovered transaction and checks an unknown hash's NotFound status.</summary>
    [Fact]
    public async Task ReadTxAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        CancellationToken ct = timeout.Token;
        LiveSample sample = await server.SampleAsync(ct);
        Assert.Equal(sample.NativeBytes, (await Client.V1Alpha.Query.ReadTxAsync(sample.Transaction.Hash.Memory, cancellationToken: ct)).Tx.NativeBytes);
        Assert.Equal(sample.NativeBytes, (await Client.V1Beta.Query.ReadTxAsync(sample.Transaction.Hash.Memory, cancellationToken: ct)).Tx.NativeBytes);
        byte[] unknown = new byte[32];
        RpcException alpha = await Assert.ThrowsAsync<RpcException>(() => Client.V1Alpha.Query.ReadTxAsync(unknown, cancellationToken: ct));
        RpcException beta = await Assert.ThrowsAsync<RpcException>(() => Client.V1Beta.Query.ReadTxAsync(unknown, cancellationToken: ct));
        Assert.Equal(StatusCode.NotFound, alpha.StatusCode);
        Assert.Equal(StatusCode.NotFound, beta.StatusCode);
    }

    /// <summary>Reads a fresh unspent output and compares address searches through both short methods.</summary>
    [Fact]
    public async Task ReadAndSearchUtxosAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        CancellationToken ct = timeout.Token;
        LiveOutput found = await server.FindUnspentOutputAsync(ct);
        Alpha.Query.AnyUtxoData alpha = Assert.Single((await Client.V1Alpha.Query.ReadUtxosAsync([found.Reference], cancellationToken: ct)).Items);
        Beta.Query.AnyUtxoData beta = Assert.Single((await Client.V1Beta.Query.ReadUtxosAsync([found.Reference], cancellationToken: ct)).Items);
        Assert.False(alpha.NativeBytes.IsEmpty);
        Assert.Equal(alpha.NativeBytes, beta.NativeBytes);
        Assert.Equal(32, alpha.TxoRef.Hash.Length);
        Assert.Equal(alpha.TxoRef.ToByteString(), beta.TxoRef.ToByteString());
        Assert.True(found.Reference.TxHash.Span.SequenceEqual(alpha.TxoRef.Hash.Span));
        Assert.Equal(found.Reference.Index, alpha.TxoRef.Index);
        // Dolos 1.6.3 ignores max_items/start_token: this checks contents, not paging.
        if (!await CheckAddressAsync(found.Address))
        {
            LiveOutput? alternative = await server.FindAlternativeOutputAsync(found.Address, ct);
            if (alternative is not null) await CheckAddressAsync(alternative.Address);
        }
        async Task<bool> CheckAddressAsync(ByteString address)
        {
            bool alphaCompared = await CheckSearchAsync(() => Client.V1Alpha.Query.SearchAllUtxosAsync(Alpha.Query.UtxoPredicate.Address(exactAddress: address.Memory), cancellationToken: ct),
                () => Client.V1Alpha.Query.SearchUtxosByAddressAsync(address.Memory, cancellationToken: ct), item => (item.TxoRef.Hash, item.TxoRef.Index, item.NativeBytes), output);
            bool betaCompared = await CheckSearchAsync(() => Client.V1Beta.Query.SearchAllUtxosAsync(Beta.Query.UtxoPredicate.Address(exactAddress: address.Memory), cancellationToken: ct),
                () => Client.V1Beta.Query.SearchUtxosByAddressAsync(address.Memory, cancellationToken: ct), item => (item.TxoRef.Hash, item.TxoRef.Index, item.NativeBytes), output);
            return alphaCompared && betaCompared;
        }
        static async Task<bool> CheckSearchAsync<T>(Func<IAsyncEnumerable<T>> all, Func<IAsyncEnumerable<T>> address, Func<T, (ByteString Hash, uint Index, ByteString Native)> fields, ITestOutputHelper log)
        {
            const int bound = 200;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                List<T> left = await TakeAsync(all(), bound + 1), right = await TakeAsync(address(), bound + 1);
                Assert.NotEmpty(left);
                Assert.All(left.Concat(right), item => { Assert.Equal(32, fields(item).Hash.Length); Assert.False(fields(item).Native.IsEmpty); });
                if (left.Count > bound || right.Count > bound)
                {
                    log.WriteLine($"Skipping {typeof(T).Namespace} address search: bound={bound}; observed counts={left.Count},{right.Count} (at most {bound + 1} read per call).");
                    return false;
                }
                // Search results are a set; compare only complete populations within the bound.
                (ByteString Hash, uint Index, ByteString Native)[] expected = Normalize(left);
                (ByteString Hash, uint Index, ByteString Native)[] actual = Normalize(right);
                if (attempt == 0 && !expected.SequenceEqual(actual)) continue;
                Assert.Equal(expected, actual);
                break;
            }
            return true;
            (ByteString Hash, uint Index, ByteString Native)[] Normalize(List<T> items) => items.Select(fields)
                .OrderBy(item => item.Hash.ToBase64(), StringComparer.Ordinal).ThenBy(item => item.Index).ToArray();
        }
    }

    /// <summary>Evaluates confirmed bytes without submitting them and logs the server's outcome.</summary>
    [Fact]
    public async Task EvalTxAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        LiveSample sample = await server.SampleAsync(timeout.Token);
        StatusCode? alpha = await OutcomeAsync(() => Client.V1Alpha.Submit.EvalTxAsync(sample.NativeBytes.Memory, timeout.Token));
        StatusCode? beta = await OutcomeAsync(() => Client.V1Beta.Submit.EvalTxAsync(sample.NativeBytes.Memory, timeout.Token));
        Assert.Equal(alpha, beta);
        output.WriteLine($"EvalTx: v1alpha={alpha?.ToString() ?? "report"}, v1beta={beta?.ToString() ?? "report"}.");
    }

    /// <summary>Checks an unknown reference's first message and cancellation on both versions.</summary>
    [Fact]
    public async Task WaitForTxAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        byte[] unknown = new byte[32];
        await CheckAsync(ct => Client.V1Alpha.Submit.WaitForTxAsync(unknown, ct), item => item.Ref);
        await CheckAsync(ct => Client.V1Beta.Submit.WaitForTxAsync(unknown, ct), item => item.Ref);
        async Task CheckAsync<T>(Func<CancellationToken, IAsyncEnumerable<T>> open, Func<T, ByteString> reference)
        {
            using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            int seen = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (T item in open(cancel.Token))
                {
                    Assert.Equal(unknown, reference(item).ToByteArray());
                    seen++;
                    cancel.Cancel();
                }
            });
            Assert.Equal(1, seen);
        }
    }

    /// <summary>Submits one invalid unfunded byte per version and records the actual status.</summary>
    [Fact]
    public async Task SubmitTxAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        byte[] invalid = [0x80];
        RpcException alpha = await Assert.ThrowsAsync<RpcException>(() => Client.V1Alpha.Submit.SubmitTxAsync(invalid, timeout.Token));
        RpcException beta = await Assert.ThrowsAsync<RpcException>(() => Client.V1Beta.Submit.SubmitTxAsync(invalid, timeout.Token));
        Assert.Equal(alpha.StatusCode, beta.StatusCode);
        output.WriteLine($"SubmitTx: v1alpha={alpha.StatusCode}, v1beta={beta.StatusCode}.");
    }

    /// <summary>Records matching mempool outcomes, including Dolos 1.6.3's known Unimplemented defect.</summary>
    [Fact]
    public async Task ReadMempoolAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        StatusCode? alpha = await OutcomeAsync(() => Client.V1Alpha.Submit.ReadMempoolAsync(timeout.Token));
        StatusCode? beta = await OutcomeAsync(() => Client.V1Beta.Submit.ReadMempoolAsync(timeout.Token));
        Assert.Equal(alpha, beta);
        output.WriteLine($"ReadMempool: v1alpha={alpha?.ToString() ?? "snapshot"}, v1beta={beta?.ToString() ?? "snapshot"}.");
    }

    private static byte[] ConfigurationBytes(IMessage message)
    {
        // Map order on the wire is unspecified; deterministic serialization compares the content.
        byte[] bytes = new byte[message.CalculateSize()];
        using CodedOutputStream stream = new(bytes) { Deterministic = true };
        message.WriteTo(stream);
        stream.CheckNoSpaceLeft();
        return bytes;
    }

    private static async Task<StatusCode?> OutcomeAsync(Func<Task> call)
    {
        try { await call(); return null; }
        catch (RpcException error) { return error.StatusCode; }
    }

    private static async Task<List<T>> TakeAsync<T>(IAsyncEnumerable<T> source, int count)
    {
        List<T> items = [];
        await foreach (T item in source)
        {
            items.Add(item);
            if (items.Count == count) break;
        }
        return items;
    }
}
