using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk;
using Beta = Utxorpc.V1Beta;

// Reads the protocol parameters, then the unspent outputs of an address.
// Usage: dotnet run --project examples/Query -- [address as hex] [policy id as hex]
string url = Environment.GetEnvironmentVariable("UTXORPC_URL") ?? "http://localhost:50051";
string? apiKey = Environment.GetEnvironmentVariable("DMTR_API_KEY");
using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
CancellationToken ct = cancellation.Token;

Dictionary<string, string>? headers = apiKey is null ? null : new() { ["dmtr-api-key"] = apiKey };
using UtxorpcClient u5c = new(url, headers);
try
{
    Beta.Cardano.PParams parameters = (await u5c.V1Beta.Query.ReadParamsAsync(
        FieldMask.FromFieldNumbers<Beta.Query.ReadParamsResponse>(Beta.Query.ReadParamsResponse.ValuesFieldNumber), ct)).Values.Cardano;
    Console.WriteLine($"Protocol version {parameters.ProtocolVersion.Major}.{parameters.ProtocolVersion.Minor}, " +
        $"largest transaction {parameters.MaxTxSize} bytes");
    if (args.Length == 0)
    {
        Console.WriteLine("Pass an address as hex to list its unspent outputs.");
        return;
    }

    // Addresses are raw bytes. The search reads every page.
    byte[] address = Convert.FromHexString(args[0]);
    Beta.Query.AnyUtxoData? first = null;
    int count = 0;
    IAsyncEnumerable<Beta.Query.AnyUtxoData> outputs = args.Length > 1
        ? u5c.V1Beta.Query.SearchAllUtxosAsync(
            Beta.Query.UtxoPredicate.Match(exactAddress: address, policyId: Convert.FromHexString(args[1])), cancellationToken: ct)
        : u5c.V1Beta.Query.SearchUtxosByAddressAsync(address, cancellationToken: ct);
    await foreach (Beta.Query.AnyUtxoData output in outputs)
    {
        Console.WriteLine($"{Convert.ToHexString(output.TxoRef.Hash.Span)}#{output.TxoRef.Index}: {output.NativeBytes.Length} bytes");
        first ??= output;
        count++;
    }
    Console.WriteLine($"Unspent outputs: {count}");
    if (first is null) return;

    // Read one output again by its reference, then the transaction that made it.
    OutputReference reference = new(first.TxoRef.Hash.Memory, first.TxoRef.Index);
    Beta.Query.ReadUtxosResponse byReference = await u5c.V1Beta.Query.ReadUtxosAsync([reference], cancellationToken: ct);
    Console.WriteLine($"Read by reference: {byReference.Items.Count}");
    Beta.Query.ReadTxResponse transaction = await u5c.V1Beta.Query.ReadTxAsync(reference.TxHash, cancellationToken: ct);
    Console.WriteLine($"Its transaction: {transaction.Tx.NativeBytes.Length} bytes");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Stopped.");
}
