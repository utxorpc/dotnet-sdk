using System.Diagnostics;
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
            if (server.IsDingo && parsed is not null) output.WriteLine("Dingo field mask not honoured.");
            else
            {
                Assert.Null(parsed);
                if (server.IsDingo) output.WriteLine("Dingo field mask honoured.");
            }
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
        if (server.IsDingo)
        {
            Assert.All(first.Block.Concat(second.Block), block => Assert.False(block.NativeBytes.IsEmpty));
            Assert.Equal(4, first.Block.Concat(second.Block).Select(block => block.NativeBytes).Distinct().Count());
            NoteHeaders("v1alpha", alphaFirst.Block.Concat(alphaSecond.Block).Select(block => block.Cardano?.Header is not null));
            NoteHeaders("v1beta", first.Block.Concat(second.Block).Select(block => block.Cardano?.Header is not null));
        }
        else Assert.DoesNotContain(second.Block, block => first.Block.Any(previous => previous.Cardano.Header.Hash == block.Cardano.Header.Hash));
        ByteString[] expected = first.Block.Concat(second.Block).Select(block => block.NativeBytes).ToArray();
        BlockPoint point = new(start.Slot, start.Hash.Memory);
        Assert.Equal(expected, (await TakeAsync(Client.V1Alpha.Sync.DumpAllHistoryAsync(point, 2, cancellationToken: ct), 4)).Select(block => block.NativeBytes));
        Assert.Equal(expected, (await TakeAsync(Client.V1Beta.Sync.DumpAllHistoryAsync(point, 2, cancellationToken: ct), 4)).Select(block => block.NativeBytes));
        void NoteHeaders(string version, IEnumerable<bool> headers) => output.WriteLine($"Dingo {version} history headers: " +
            string.Join(", ", headers.Select((present, index) => $"page {index / 2 + 1} block {index % 2 + 1}={(present ? "present" : "missing")}")));
        async Task<Beta.Sync.BlockRef> FindStartAsync()
        {
            if (server.IsDingo)
            {
                Beta.Sync.DumpHistoryResponse oldest = await Client.V1Beta.Sync.DumpHistoryAsync(new() { MaxItems = 1 }, cancellationToken: ct);
                Beta.Sync.AnyChainBlock block = Assert.Single(oldest.Block);
                // Dingo omits parsed headers on the first Byron-era block and all four sampled post-start blocks, on both versions.
                Assert.NotNull(oldest.NextToken);
                Assert.Equal(32, oldest.NextToken.Hash.Length);
                output.WriteLine($"Dingo first Byron-era block: parsed Cardano.Header {(block.Cardano?.Header is null ? "missing" : "present")}; start point from the complete next token. Dingo starts after it.");
                return oldest.NextToken;
            }
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
        List<V1Alpha.FollowTipEvent> alpha = await TakeAsync(Client.V1Alpha.Sync.FollowTipAsync(null, cancellationToken: timeout.Token), 2);
        List<V1Beta.FollowTipEvent> beta = await TakeAsync(Client.V1Beta.Sync.FollowTipAsync(null, cancellationToken: timeout.Token), 2);
        Assert.Equal(2, alpha.Count);
        Assert.Equal(2, beta.Count);
        foreach (V1Alpha.FollowTipEvent item in alpha) output.WriteLine($"alpha: {item switch { V1Alpha.BlockApplied _ => "apply", V1Alpha.BlockUndone _ => "undo", V1Alpha.Reset _ => "reset" }}; tip={item.Tip is not null}.");
        foreach (V1Beta.FollowTipEvent item in beta) output.WriteLine($"beta: {item switch { V1Beta.BlockApplied _ => "apply", V1Beta.BlockUndone _ => "undo", V1Beta.Reset _ => "reset" }}; tip={item.Tip is not null}.");
    }

    /// <summary>Resumes from a current intersection without replaying older blocks.</summary>
    [Theory]
    [InlineData("v1alpha")]
    [InlineData("v1beta")]
    public async Task FollowTipFromIntersectAsync(string version)
    {
        using CancellationTokenSource timeout = Timeout();
        Beta.Sync.BlockRef tip = (await Client.V1Beta.Sync.ReadTipAsync(timeout.Token)).Tip;
        BlockPoint point = new(tip.Slot, tip.Hash.Memory);
        if (version == "v1alpha")
        {
            Alpha.Sync.FollowTipResponse first = Assert.Single(await TakeAsync(Client.V1Alpha.Sync.FollowTipAsync(point, cancellationToken: timeout.Token), 1)).Response;
            await CheckAsync(first.Reset?.Slot, first.Reset?.Hash, first.Apply?.Cardano?.Header?.Slot, first.Apply?.Cardano?.Header?.Height);
        }
        else
        {
            Beta.Sync.FollowTipResponse first = Assert.Single(await TakeAsync(Client.V1Beta.Sync.FollowTipAsync(point, cancellationToken: timeout.Token), 1)).Response;
            await CheckAsync(first.Reset?.Slot, first.Reset?.Hash, first.Apply?.Cardano?.Header?.Slot, first.Apply?.Cardano?.Header?.Height);
        }
        async Task CheckAsync(ulong? resetSlot, ByteString? resetHash, ulong? applySlot, ulong? applyHeight)
        {
            if (resetSlot is not null)
            {
                Assert.Equal(tip.Slot, resetSlot);
                Assert.Equal(tip.Hash, resetHash);
                return;
            }
            Assert.True(applySlot > tip.Slot, "The first event must reset at the intersection or apply its next block.");
            Beta.Cardano.BlockHeader previous = Assert.Single((await Client.V1Beta.Sync.FetchBlockAsync(point, cancellationToken: timeout.Token)).Block).Cardano.Header;
            Assert.Equal(previous.Height + 1, applyHeight);
        }
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
        FieldMask mask = FieldMask.FromString("values");
        Alpha.Query.ReadParamsResponse alphaMasked = await Client.V1Alpha.Query.ReadParamsAsync(mask, ct);
        Beta.Query.ReadParamsResponse betaMasked = await Client.V1Beta.Query.ReadParamsAsync(mask, ct);
        Assert.Equal(ConfigurationBytes(alpha.Values), ConfigurationBytes(alphaMasked.Values));
        Assert.Equal(ConfigurationBytes(beta.Values), ConfigurationBytes(betaMasked.Values));
        CheckMask(alphaMasked.LedgerTip);
        CheckMask(betaMasked.LedgerTip);
        void CheckMask(IMessage? ledgerTip)
        {
            if (server.IsDingo && ledgerTip is not null) output.WriteLine("Dingo ReadParams field mask not honoured: ledger_tip remains.");
            else Assert.Null(ledgerTip);
        }
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
        await foreach (V1Alpha.WatchTxEvent item in Client.V1Alpha.Watch.WatchTxAsync(null, null, cancellationToken: timeout.Token))
        {
            if (item is not V1Alpha.TxApplied applied) continue;
            Assert.Equal(32, applied.Tx.Cardano.Hash.Length);
            alphaFound = true;
            break;
        }
        await foreach (V1Beta.WatchTxEvent item in Client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: timeout.Token))
        {
            if (item is not V1Beta.TxApplied applied) continue;
            Assert.Equal(32, applied.Tx.Cardano.Hash.Length);
            betaFound = true;
            break;
        }
        Assert.True(alphaFound && betaFound);
    }

    /// <summary>Accepts an address predicate and intersection, validates delivered matches, and cancels cleanly.</summary>
    [Theory]
    [InlineData("v1alpha")]
    [InlineData("v1beta")]
    public async Task WatchTxFromIntersectWithPredicateAsync(string version)
    {
        using CancellationTokenSource timeout = Timeout();
        ByteString address = (await server.SampleAsync(timeout.Token)).Transaction.Outputs.First(item => !item.Address.IsEmpty).Address;
        Beta.Sync.BlockRef tip = (await Client.V1Beta.Sync.ReadTipAsync(timeout.Token)).Tip;
        BlockPoint point = new(tip.Slot, tip.Hash.Memory);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        cancel.CancelAfter(TimeSpan.FromSeconds(3));
        int seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (version == "v1alpha")
            {
                await foreach (V1Alpha.WatchTxEvent item in Client.V1Alpha.Watch.WatchTxAsync(Alpha.Watch.TxPredicate.HasAddress(exactAddress: address.Memory), point, cancellationToken: cancel.Token))
                {
                    Alpha.Cardano.Tx? tx = item.Response.Apply?.Cardano ?? item.Response.Undo?.Cardano;
                    if (tx is not null) Assert.Contains(address, tx.Outputs.Select(value => value.Address)
                        .Concat(tx.Inputs.Concat(tx.ReferenceInputs).Concat(tx.Collateral?.Collateral_ ?? []).Select(value => value.AsOutput?.Address ?? ByteString.Empty))
                        .Append(tx.Collateral?.CollateralReturn?.Address ?? ByteString.Empty));
                    if (++seen == 2) cancel.Cancel();
                }
            }
            else
            {
                await foreach (V1Beta.WatchTxEvent item in Client.V1Beta.Watch.WatchTxAsync(Beta.Watch.TxPredicate.HasAddress(exactAddress: address.Memory), point, cancellationToken: cancel.Token))
                {
                    Beta.Cardano.Tx? tx = item.Response.Apply?.Cardano ?? item.Response.Undo?.Cardano;
                    if (tx is not null) Assert.Contains(address, tx.Outputs.Select(value => value.Address)
                        .Concat(tx.Inputs.Concat(tx.ReferenceInputs).Concat(tx.Collateral?.Collateral_ ?? []).Select(value => value.AsOutput?.Address ?? ByteString.Empty))
                        .Append(tx.Collateral?.CollateralReturn?.Address ?? ByteString.Empty));
                    if (++seen == 2) cancel.Cancel();
                }
            }
        });
        Assert.False(timeout.IsCancellationRequested);
        output.WriteLine($"{version} filtered WatchTx: {seen} events before cancellation.");
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
        UtxorpcException alpha = await Assert.ThrowsAsync<UtxorpcException>(() => Client.V1Alpha.Query.ReadTxAsync(unknown, cancellationToken: ct));
        UtxorpcException beta = await Assert.ThrowsAsync<UtxorpcException>(() => Client.V1Beta.Query.ReadTxAsync(unknown, cancellationToken: ct));
        Assert.Equal(UtxorpcErrorKind.NotFound, alpha.Kind);
        Assert.Equal(UtxorpcErrorKind.NotFound, beta.Kind);
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
        FieldMask mask = FieldMask.FromString("items");
        Alpha.Query.ReadUtxosResponse alphaMasked = await Client.V1Alpha.Query.ReadUtxosAsync([found.Reference], mask, ct);
        Beta.Query.ReadUtxosResponse betaMasked = await Client.V1Beta.Query.ReadUtxosAsync([found.Reference], mask, ct);
        Assert.Equal(alpha.NativeBytes, Assert.Single(alphaMasked.Items).NativeBytes);
        Assert.Equal(beta.NativeBytes, Assert.Single(betaMasked.Items).NativeBytes);
        CheckMask(alphaMasked.LedgerTip);
        CheckMask(betaMasked.LedgerTip);
        void CheckMask(IMessage? ledgerTip)
        {
            // Both servers currently ignore ReadUtxos masks; retain the selected data checks.
            if (ledgerTip is not null) output.WriteLine($"{(server.IsDingo ? "Dingo" : "Dolos")} ReadUtxos field mask not honoured: ledger_tip remains.");
            else Assert.Null(ledgerTip);
        }
        // Dolos 1.6.3 ignores max_items/start_token: this checks contents, not paging.
        bool compared = await CheckAddressAsync(found.Address);
        if (!compared)
        {
            LiveOutput? alternative = await server.FindAlternativeOutputAsync(found.Address, ct);
            if (alternative is not null) compared = await CheckAddressAsync(alternative.Address);
        }
        Assert.True(compared, "No address had at most 200 outputs; the observed search counts are in the test output.");
        output.WriteLine("Compared complete address searches in both versions.");
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

    /// <summary>Checks payment, delegation and policy predicates against live output contents and exact-address results.</summary>
    [Theory]
    [InlineData("v1alpha")]
    [InlineData("v1beta")]
    public async Task UtxoPredicatesAsync(string version)
    {
        using CancellationTokenSource timeout = Timeout();
        CancellationToken ct = timeout.Token;
        LiveOutput found = await server.FindUnspentOutputAsync(ct, item => item.Address.Length >= 29 && item.Address[0] >> 4 <= 7
            && item.Assets.Any(group => group.PolicyId.Length == 28));
        Beta.Cardano.TxOutput unspent = Assert.Single((await Client.V1Beta.Query.ReadUtxosAsync([found.Reference], cancellationToken: ct)).Items).Cardano;
        ByteString policy = unspent.Assets.First(group => group.PolicyId.Length == 28).PolicyId;
        ReadOnlyMemory<byte> payment = found.Address.Memory.Slice(1, 28);
        const int bound = 32;
        int returnedKinds = 0;
        var exact = (await SearchAsync("exact", Alpha.Query.UtxoPredicate.Address(exactAddress: found.Address.Memory), Beta.Query.UtxoPredicate.Address(exactAddress: found.Address.Memory))).Items;
        Assert.NotEmpty(exact);
        Assert.Contains(exact, item => item.HasPolicy);
        Assert.All(exact, item => Assert.Equal(found.Address, item.Address));
        var byPayment = await SearchAsync("payment", Alpha.Query.UtxoPredicate.Address(paymentPart: payment), Beta.Query.UtxoPredicate.Address(paymentPart: payment));
        if (!byPayment.TimedOut) Assert.NotEmpty(byPayment.Items);
        Assert.All(byPayment.Items, item => Assert.True(item.Address.Length >= 29 && item.Address.Span.Slice(1, 28).SequenceEqual(payment.Span)));
        CheckConsistency(byPayment.Items, "payment");
        if (found.Address.Length == 57 && found.Address[0] >> 4 <= 3)
        {
            ReadOnlyMemory<byte> delegation = found.Address.Memory.Slice(29, 28);
            var byDelegation = await SearchAsync("delegation", Alpha.Query.UtxoPredicate.Address(delegationPart: delegation), Beta.Query.UtxoPredicate.Address(delegationPart: delegation));
            if (!server.IsDingo && byDelegation.Items.Count == 0)
                output.WriteLine("Dolos delegation search returned no outputs despite exact-address and payment matches (server gap).");
            else if (!byDelegation.TimedOut) Assert.NotEmpty(byDelegation.Items);
            Assert.All(byDelegation.Items, item => Assert.True(item.Address.Length == 57 && item.Address.Span.Slice(29, 28).SequenceEqual(delegation.Span)));
            CheckConsistency(byDelegation.Items, "delegation");
        }
        // Narrow the asset search to the discovered address instead of scanning a server's entire UTxO set.
        Alpha.Query.UtxoPredicate alphaAsset = Alpha.Query.UtxoPredicate.Match(exactAddress: found.Address.Memory, policyId: policy.Memory);
        Beta.Query.UtxoPredicate betaAsset = Beta.Query.UtxoPredicate.Match(exactAddress: found.Address.Memory, policyId: policy.Memory);
        var byAsset = await SearchAsync("asset", alphaAsset, betaAsset);
        if (!byAsset.TimedOut) Assert.NotEmpty(byAsset.Items);
        Assert.All(byAsset.Items, item => { Assert.True(item.HasPolicy); Assert.Equal(found.Address, item.Address); });
        CheckConsistency(byAsset.Items, "asset");
        if (server.IsDingo && returnedKinds == 0) output.WriteLine($"Dingo {version}: no predicate kind returned any outputs within its single 15-second attempt.");
        async Task<(List<(ByteString Ref, ByteString Address, ByteString Bytes, bool HasPolicy)> Items, bool TimedOut)> SearchAsync(
            string kind, Alpha.Query.UtxoPredicate alpha, Beta.Query.UtxoPredicate beta)
        {
            using CancellationTokenSource search = CancellationTokenSource.CreateLinkedTokenSource(ct);
            search.CancelAfter(TimeSpan.FromSeconds(15));
            long started = Stopwatch.GetTimestamp();
            List<(ByteString Ref, ByteString Address, ByteString Bytes, bool HasPolicy)> items = [];
            bool timedOut = false;
            try
            {
                if (version == "v1alpha")
                    await TakeAsync(Client.V1Alpha.Query.SearchAllUtxosAsync(alpha, pageSize: 4, cancellationToken: search.Token), bound + 1,
                        item => (item.TxoRef.ToByteString(), item.Cardano.Address, item.NativeBytes, item.Cardano.Assets.Any(group => group.PolicyId == policy)), items);
                else
                    await TakeAsync(Client.V1Beta.Query.SearchAllUtxosAsync(beta, pageSize: 4, cancellationToken: search.Token), bound + 1,
                        item => (item.TxoRef.ToByteString(), item.Cardano.Address, item.NativeBytes, item.Cardano.Assets.Any(group => group.PolicyId == policy)), items);
            }
            catch (OperationCanceledException) when (server.IsDingo && kind != "exact" && search.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Dingo v0.94.1 api/utxorpc/query.go:574-583 documents full live-UTxO scans for asset-only predicates.
                timedOut = true;
                output.WriteLine($"Dingo {version} {kind} predicate server gap: budget expired after {Stopwatch.GetElapsedTime(started).TotalSeconds:F1} s; no retry.");
            }
            if (kind != "exact" && items.Count > 0) returnedKinds++;
            return (items, timedOut);
        }
        void CheckConsistency(List<(ByteString Ref, ByteString Address, ByteString Bytes, bool HasPolicy)> matches, string predicate)
        {
            // Queries are not atomic snapshots; compare immutable outputs present in both bounded sets.
            var shared = matches.Join(exact, item => item.Ref, item => item.Ref, (left, right) => (left, right)).ToList();
            Assert.All(shared, pair => { Assert.Equal(pair.right.Address, pair.left.Address); Assert.Equal(pair.right.Bytes, pair.left.Bytes); });
            output.WriteLine($"{version} {predicate}: {matches.Count} outputs, {shared.Count} exact-address overlaps; enumeration bound {bound + 1}, page size 4.");
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
            if (server.IsDingo) cancel.CancelAfter(TimeSpan.FromSeconds(5));
            int seen = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (T item in open(cancel.Token))
                {
                    seen++;
                    if (!server.IsDingo)
                    {
                        Assert.Equal(unknown, reference(item).ToByteArray());
                        cancel.Cancel();
                    }
                }
            });
            Assert.Equal(server.IsDingo ? 0 : 1, seen);
            if (server.IsDingo) output.WriteLine("Dingo WaitForTx: no message for an unknown reference before cancellation after 5 seconds.");
        }
    }

    /// <summary>Submits one invalid unfunded byte per version and records the actual status.</summary>
    [Fact]
    public async Task SubmitTxAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        byte[] invalid = [0x80];
        UtxorpcException alpha = await Assert.ThrowsAsync<UtxorpcException>(() => Client.V1Alpha.Submit.SubmitTxAsync(invalid, timeout.Token));
        UtxorpcException beta = await Assert.ThrowsAsync<UtxorpcException>(() => Client.V1Beta.Submit.SubmitTxAsync(invalid, timeout.Token));
        Assert.Equal(UtxorpcErrorKind.InvalidTx, alpha.Kind);
        Assert.Equal(UtxorpcErrorKind.InvalidTx, beta.Kind);
        Assert.Equal(alpha.StatusCode, beta.StatusCode);
        output.WriteLine($"SubmitTx: v1alpha={alpha.StatusCode}, v1beta={beta.StatusCode}.");
    }

    /// <summary>Records matching mempool outcomes, including Dolos 1.6.3's known Unimplemented defect.</summary>
    [Fact]
    public async Task ReadMempoolAsync()
    {
        using CancellationTokenSource timeout = Timeout();
        if (server.IsDingo)
        {
            Alpha.Submit.ReadMempoolResponse alphaSnapshot = await Client.V1Alpha.Submit.ReadMempoolAsync(timeout.Token);
            Beta.Submit.ReadMempoolResponse betaSnapshot = await Client.V1Beta.Submit.ReadMempoolAsync(timeout.Token);
            CheckSnapshot(alphaSnapshot.Items.Select(item => (item.NativeBytes, item.Stage == Alpha.Submit.Stage.Mempool, item.Ref)));
            CheckSnapshot(betaSnapshot.Items.Select(item => (item.NativeBytes, item.Stage == Beta.Submit.Stage.Mempool, item.Ref)));
            output.WriteLine($"Dingo ReadMempool: v1alpha={alphaSnapshot.Items.Count} items, v1beta={betaSnapshot.Items.Count} items.");
            return;
        }
        StatusCode? alpha = await OutcomeAsync(() => Client.V1Alpha.Submit.ReadMempoolAsync(timeout.Token));
        StatusCode? beta = await OutcomeAsync(() => Client.V1Beta.Submit.ReadMempoolAsync(timeout.Token));
        Assert.Equal(alpha, beta);
        output.WriteLine($"ReadMempool: v1alpha={alpha?.ToString() ?? "snapshot"}, v1beta={beta?.ToString() ?? "snapshot"}.");
        void CheckSnapshot(IEnumerable<(ByteString Bytes, bool Mempool, ByteString Ref)> items) => Assert.All(items, item =>
        {
            Assert.False(item.Bytes.IsEmpty);
            Assert.True(item.Mempool);
            if (item.Ref.IsEmpty) output.WriteLine("Dingo ReadMempool leaves ref unset.");
            else Assert.Equal(32, item.Ref.Length);
        });
    }

    /// <summary>Opens an unfiltered mempool stream and requires cancellation unless the server lacks the RPC.</summary>
    [Theory]
    [InlineData("v1alpha")]
    [InlineData("v1beta")]
    public async Task WatchMempoolAsync(string version)
    {
        using CancellationTokenSource timeout = Timeout();
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        cancel.CancelAfter(TimeSpan.FromSeconds(3));
        int seen = 0;
        Exception error = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            if (version == "v1alpha")
            {
                await foreach (Alpha.Submit.WatchMempoolResponse _ in Client.V1Alpha.Submit.WatchMempoolAsync(null, cancellationToken: cancel.Token))
                    if (++seen == 2) cancel.Cancel();
            }
            else
            {
                await foreach (Beta.Submit.WatchMempoolResponse _ in Client.V1Beta.Submit.WatchMempoolAsync(null, cancellationToken: cancel.Token))
                    if (++seen == 2) cancel.Cancel();
            }
        });
        if (error is RpcException { StatusCode: StatusCode.Unimplemented })
            output.WriteLine($"{(server.IsDingo ? "Dingo" : "Dolos")} {version} WatchMempool is unimplemented (server defect).");
        else Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.False(timeout.IsCancellationRequested);
        output.WriteLine($"{version} WatchMempool: {seen} events before {error.GetType().Name}.");
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

    internal static Task<List<T>> TakeAsync<T>(IAsyncEnumerable<T> source, int count) => TakeAsync(source, count, static item => item, []);

    private static async Task<List<TResult>> TakeAsync<T, TResult>(IAsyncEnumerable<T> source, int count, Func<T, TResult> project, List<TResult> items)
    {
        await foreach (T item in source)
        {
            items.Add(project(item));
            if (items.Count == count) break;
        }
        return items;
    }
}
