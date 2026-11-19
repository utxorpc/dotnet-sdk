using Google.Protobuf;
using Xunit.Abstractions;
using Xunit.Sdk;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test.Live;

/// <summary>Exercises state, datum lookup and real output paging on Dingo.</summary>
[Trait("Category", "LiveDingo")]
public sealed class DingoTests(LiveServer server, ITestOutputHelper output) : IClassFixture<LiveServer>
{
    private readonly LiveServer fixture = server;
    private readonly UtxorpcClient client = server.IsDingo ? server.Client
        : throw new InvalidOperationException("Set UTXORPC_SERVER=dingo to run Dingo-only tests.");

    /// <summary>Reads Dingo's supported stake-pool distribution state query.</summary>
    [Fact]
    public async Task ReadStateAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
        Beta.Query.ReadStateResponse response = await client.V1Beta.Query.ReadStateAsync(
            new Beta.Cardano.StateQuery { StakePoolDistribution = new() }, cancellationToken: timeout.Token);
        Assert.NotNull(response.Result?.Cardano?.StakePoolDistribution);
        fixture.Check("ReadState", true);
        output.WriteLine($"Dingo ReadState: {response.CalculateSize()} response bytes.");
    }

    /// <summary>Reads a datum discovered from Dingo rather than a fixed transaction.</summary>
    [Fact]
    public async Task ReadDataAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
        ByteString? hash = null;
        await FindOutputsAsync(outputs =>
        {
            // Dingo v0.94.1 uses gouroboros v0.220.0: ledger/babbage/babbage.go:1213-1215 leaves Datum.OriginalCbor unset.
            Beta.Cardano.Datum? datum = outputs.Select(item => item.Datum)
                .FirstOrDefault(item => item is not null && !item.Hash.IsEmpty);
            hash = datum?.Hash;
            return Task.FromResult(hash is not null);
        }, timeout.Token);
        Assert.NotNull(hash);
        Alpha.Query.AnyChainDatum alpha = Assert.Single((await client.V1Alpha.Query.ReadDataAsync([hash.Memory], cancellationToken: timeout.Token)).Values);
        Beta.Query.AnyChainDatum beta = Assert.Single((await client.V1Beta.Query.ReadDataAsync([hash.Memory], cancellationToken: timeout.Token)).Values);
        Assert.Equal(hash, alpha.Key);
        Assert.Equal(hash, beta.Key);
        Assert.False(alpha.NativeBytes.IsEmpty);
        Assert.Equal(alpha.NativeBytes, beta.NativeBytes);
        fixture.Check("ReadData", true);
        output.WriteLine($"Dingo ReadData: {alpha.NativeBytes.Length} native bytes on each version.");
    }

    /// <summary>Matches both versions' paging helpers to two real raw pages in order.</summary>
    [Fact]
    public async Task SearchUtxosPagingAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
        CancellationToken ct = timeout.Token;
        ByteString? address = null;
        int tried = 0;
        Beta.Query.SearchUtxosResponse sample = await client.V1Beta.Query.SearchUtxosAsync(new() { MaxItems = 3 }, cancellationToken: ct);
        foreach (ByteString candidate in sample.Items.Select(item => item.Cardano.Address).Where(value => !value.IsEmpty).Distinct())
        {
            tried++;
            Beta.Query.SearchUtxosResponse page = await client.V1Beta.Query.SearchUtxosAsync(new()
            { Predicate = Beta.Query.UtxoPredicate.Address(exactAddress: candidate.Memory), MaxItems = 2 }, cancellationToken: ct);
            if (page.HasNextToken && page.NextToken.Length > 0) { address = candidate; break; }
        }
        Assert.NotNull(address);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Alpha.Query.UtxoPredicate alphaPredicate = Alpha.Query.UtxoPredicate.Address(exactAddress: address.Memory);
            Beta.Query.UtxoPredicate betaPredicate = Beta.Query.UtxoPredicate.Address(exactAddress: address.Memory);
            Alpha.Query.SearchUtxosResponse alphaFirst = await client.V1Alpha.Query.SearchUtxosAsync(new() { Predicate = alphaPredicate, MaxItems = 2 }, cancellationToken: ct);
            Beta.Query.SearchUtxosResponse betaFirst = await client.V1Beta.Query.SearchUtxosAsync(new() { Predicate = betaPredicate, MaxItems = 2 }, cancellationToken: ct);
            Assert.Equal(2, alphaFirst.Items.Count);
            Assert.Equal(2, betaFirst.Items.Count);
            Assert.NotEmpty(alphaFirst.NextToken);
            Assert.True(betaFirst.HasNextToken && betaFirst.NextToken.Length > 0);
            Alpha.Query.SearchUtxosResponse alphaSecond = await client.V1Alpha.Query.SearchUtxosAsync(new() { Predicate = alphaPredicate, StartToken = alphaFirst.NextToken, MaxItems = 2 }, cancellationToken: ct);
            Beta.Query.SearchUtxosResponse betaSecond = await client.V1Beta.Query.SearchUtxosAsync(new() { Predicate = betaPredicate, StartToken = betaFirst.NextToken, MaxItems = 2 }, cancellationToken: ct);
            Assert.InRange(alphaSecond.Items.Count, 1, 2);
            Assert.InRange(betaSecond.Items.Count, 1, 2);
            ByteString[] alphaExpected = alphaFirst.Items.Concat(alphaSecond.Items).Select(item => item.TxoRef.ToByteString()).ToArray();
            ByteString[] betaExpected = betaFirst.Items.Concat(betaSecond.Items).Select(item => item.TxoRef.ToByteString()).ToArray();
            ByteString[] alphaActual = (await LiveTests.TakeAsync(client.V1Alpha.Query.SearchUtxosByAddressAsync(address.Memory, pageSize: 2, cancellationToken: ct), alphaExpected.Length)).Select(item => item.TxoRef.ToByteString()).ToArray();
            ByteString[] betaActual = (await LiveTests.TakeAsync(client.V1Beta.Query.SearchUtxosByAddressAsync(address.Memory, pageSize: 2, cancellationToken: ct), betaExpected.Length)).Select(item => item.TxoRef.ToByteString()).ToArray();
            if (attempt == 0 && (!alphaExpected.SequenceEqual(alphaActual) || !betaExpected.SequenceEqual(betaActual))) continue;
            Assert.Equal(alphaExpected, alphaActual);
            Assert.Equal(betaExpected, betaActual);
            fixture.Check("SearchUtxos paging", true);
            output.WriteLine($"Dingo SearchUtxos: raw pages and short method match {alphaActual.Length}/{betaActual.Length} references; {tried} distinct addresses tried; a nonempty token supplied the second page.");
            break;
        }
    }

    private async Task FindOutputsAsync(Func<IReadOnlyList<Beta.Cardano.TxOutput>, Task<bool>> accept, CancellationToken cancellationToken)
    {
        using CancellationTokenSource scan = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scan.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            string? token = null;
            for (int page = 0; page < 20; page++)
            {
                Beta.Query.SearchUtxosRequest request = new() { MaxItems = 100 };
                if (token is not null) request.StartToken = token;
                Beta.Query.SearchUtxosResponse response = await client.V1Beta.Query.SearchUtxosAsync(request, cancellationToken: scan.Token);
                if (await accept(response.Items.Select(item => item.Cardano).ToArray())) return;
                if (!response.HasNextToken || response.NextToken.Length == 0) break;
                token = response.NextToken;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Dingo UTxO input scan reached its 30-second budget; trying WatchTx.");
        }
        try
        {
            await foreach (V1Beta.WatchTxEvent item in client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: cancellationToken))
            {
                if (item is not V1Beta.TxApplied { Tx.Cardano: { } tx }) continue;
                await fixture.ReadIndexedAsync(tx.Hash, cancellationToken);
                if (await accept(tx.Outputs)) return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new XunitException("No suitable output was found in Dingo's UTxO set or WatchTx within 180 seconds.");
        }
        throw new XunitException("WatchTx ended without a suitable output after searching Dingo's UTxO set.");
    }
}
