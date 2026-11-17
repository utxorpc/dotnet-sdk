using Utxorpc.Sdk;
using Beta = Utxorpc.V1Beta;

// Evaluates a transaction, and submits it only with --submit.
// Usage: dotnet run --project examples/Submit -- [signed transaction as hex] [--submit]
//
// Nothing is spent by default. Without --submit the transaction is only evaluated. Without a
// transaction, a built-in one is used that no ledger accepts: it spends an output that does not
// exist and carries no signature. Funds move only when you pass a transaction you signed yourself
// together with --submit; the network then charges that transaction's fee.
const string Unspendable = "84a300818258200000000000000000000000000000000000000000000000000000000000000000000181" +
    "82581d60000000000000000000000000000000000000000000000000000000001a000f4240021a00030d40a0f5f6";
string url = Environment.GetEnvironmentVariable("UTXORPC_URL") ?? "http://localhost:50051";
string? apiKey = Environment.GetEnvironmentVariable("DMTR_API_KEY");
bool submit = args.Contains("--submit");
byte[] tx = Convert.FromHexString(args.FirstOrDefault(argument => argument != "--submit") ?? Unspendable);
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
    if (!submit)
    {
        Beta.Submit.EvalTxResponse evaluation = await u5c.V1Beta.Submit.EvalTxAsync(tx, ct);
        Console.WriteLine($"Evaluation: {evaluation.Report}");
        return;
    }

    Beta.Submit.SubmitTxResponse submitted = await u5c.V1Beta.Submit.SubmitTxAsync(tx, ct);
    Console.WriteLine($"Submitted: {Convert.ToHexString(submitted.Ref.Span)}");

    // SubmitTx is never retried; follow the transaction instead of sending it again.
    await foreach (Beta.Submit.WaitForTxResponse update in u5c.V1Beta.Submit.WaitForTxAsync(submitted.Ref.Memory, ct))
    {
        Console.WriteLine(update.Stage);
        if (update.Stage == Beta.Submit.Stage.Confirmed) break;
    }
}
catch (UtxorpcException error)
{
    string outcome = error.Kind == UtxorpcErrorKind.InvalidTx ? "Rejected" : "Failed";
    Console.WriteLine($"{outcome} ({error.Kind}): {error.Detail}");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Stopped.");
}
