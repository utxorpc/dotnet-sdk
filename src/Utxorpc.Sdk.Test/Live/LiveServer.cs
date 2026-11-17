using Google.Protobuf;
using Grpc.Core;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test.Live;

/// <summary>Stores an immutable transaction's bytes and its parsed outputs for discovery.</summary>
public sealed record LiveSample(Beta.Cardano.Tx Transaction, ByteString NativeBytes);

/// <summary>Identifies an output and the address observed while it was unspent.</summary>
public sealed record LiveOutput(OutputReference Reference, ByteString Address);

/// <summary>Shares one configured client and a lazily discovered transaction across live tests.</summary>
public sealed class LiveServer : IDisposable
{
    private readonly Lazy<Task<LiveSample>> sample;

    /// <summary>Reads the live endpoint and optional headers without disclosing header values.</summary>
    public LiveServer()
    {
        IsDingo = Environment.GetEnvironmentVariable("UTXORPC_SERVER") switch
        {
            null or "dolos" => false,
            "dingo" => true,
            _ => throw new InvalidOperationException("UTXORPC_SERVER must be dolos or dingo.")
        };
        string? url = Environment.GetEnvironmentVariable("UTXORPC_URL");
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Set UTXORPC_URL and, if needed, UTXORPC_HEADERS to run live tests.");
        List<KeyValuePair<string, string>> headers = [];
        foreach (string entry in (Environment.GetEnvironmentVariable("UTXORPC_HEADERS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = entry.IndexOf('=');
            if (separator <= 0) throw new InvalidOperationException("UTXORPC_HEADERS must contain name=value pairs separated by semicolons.");
            headers.Add(new(entry[..separator], entry[(separator + 1)..]));
        }
        try
        {
            // Dolos 1.6.3 ignores SearchUtxos max_items and can return more than 4 MB.
            Client = new UtxorpcClient(url, headers, configureChannel: options => options.MaxReceiveMessageSize = null);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Check UTXORPC_URL and UTXORPC_HEADERS; their values are not displayed.");
        }
        sample = new(() => DiscoverAsync(CancellationToken.None));
    }

    /// <summary>Gets whether the selected live server is Dingo rather than Dolos.</summary>
    public bool IsDingo { get; }

    /// <summary>Gets the shared client for both protocol versions.</summary>
    public UtxorpcClient Client { get; }

    /// <summary>Gets one discovery shared by tests without depending on their execution order.</summary>
    public Task<LiveSample> SampleAsync(CancellationToken cancellationToken) => sample.Value.WaitAsync(cancellationToken);

    /// <summary>Finds an output that is unspent now, rechecking the cached transaction first.</summary>
    public async Task<LiveOutput> FindUnspentOutputAsync(CancellationToken cancellationToken, Func<Beta.Cardano.TxOutput, bool>? accept = null)
    {
        using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(180));
        LiveSample known = await SampleAsync(bound.Token);
        LiveOutput? found = await FindOutputAsync(known.Transaction, bound.Token, accept: accept);
        if (found is not null) return found;
        await foreach (V1Beta.WatchTxEvent item in Client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: bound.Token))
        {
            if (item is not V1Beta.TxApplied { Tx.Cardano: { } tx }) continue;
            if (IsDingo) await ReadIndexedAsync(tx.Hash, bound.Token);
            found = await FindOutputAsync(tx, bound.Token, accept: accept);
            if (found is not null) return found;
        }
        throw new InvalidOperationException("WatchTx ended without an unspent output within 180 seconds.");
    }

    /// <summary>Checks other addresses in the cached transaction without starting another watch.</summary>
    public async Task<LiveOutput?> FindAlternativeOutputAsync(ByteString excludedAddress, CancellationToken cancellationToken) =>
        await FindOutputAsync((await SampleAsync(cancellationToken)).Transaction, cancellationToken, excludedAddress);

    private async Task<LiveSample> DiscoverAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(180));
        await foreach (V1Beta.WatchTxEvent item in Client.V1Beta.Watch.WatchTxAsync(null, null, cancellationToken: bound.Token))
        {
            if (item is not V1Beta.TxApplied { Tx.Cardano: { } tx }) continue;
            Beta.Query.ReadTxResponse? indexed = IsDingo ? await ReadIndexedAsync(tx.Hash, bound.Token) : null;
            if (await FindOutputAsync(tx, bound.Token) is null) continue;
            Beta.Query.ReadTxResponse read = indexed ?? await Client.V1Beta.Query.ReadTxAsync(tx.Hash.Memory, cancellationToken: bound.Token);
            if (read.Tx.NativeBytes.IsEmpty) throw new InvalidOperationException("ReadTx returned no native transaction bytes.");
            return new(tx, read.Tx.NativeBytes);
        }
        throw new InvalidOperationException("WatchTx ended without a usable transaction within 180 seconds.");
    }

    internal async Task<Beta.Query.ReadTxResponse> ReadIndexedAsync(ByteString hash, CancellationToken cancellationToken)
    {
        // Dingo publishes WatchTx before its query index is visible (observed around 2 seconds).
        using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            while (true)
            {
                try { return await Client.V1Beta.Query.ReadTxAsync(hash.Memory, cancellationToken: bound.Token); }
                catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { }
                await Task.Delay(TimeSpan.FromMilliseconds(500), bound.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Dingo WatchTx transaction was not query-indexed within 15 seconds.");
        }
    }

    private async Task<LiveOutput?> FindOutputAsync(Beta.Cardano.Tx tx, CancellationToken cancellationToken, ByteString? excludedAddress = null,
        Func<Beta.Cardano.TxOutput, bool>? accept = null)
    {
        for (int i = tx.Outputs.Count - 1; i >= 0; i--)
        {
            if (tx.Outputs[i].Address == excludedAddress || accept is not null && !accept(tx.Outputs[i])) continue;
            OutputReference reference = new(tx.Hash.Memory, (uint)i);
            try
            {
                // Check one reference at a time: a spent reference can fail the entire call.
                Beta.Query.ReadUtxosResponse read = await Client.V1Beta.Query.ReadUtxosAsync([reference], cancellationToken: cancellationToken);
                if (read.Items.Count > 0) return new(reference, read.Items[0].Cardano.Address);
            }
            catch (RpcException) { }
        }
        return null;
    }

    /// <summary>Closes the channel created by this fixture.</summary>
    public void Dispose() => Client.Dispose();
}
