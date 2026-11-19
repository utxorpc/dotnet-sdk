using Utxorpc.Sdk;
using Beta = Utxorpc.V1Beta;

// A web API over one shared client from the Utxorpc configuration section.
// Usage: dotnet run --project examples/DependencyInjection -- --Utxorpc:Url=http://localhost:50051
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddUtxorpcClient(o =>
{
    builder.Configuration.GetSection("Utxorpc").Bind(o);
    // Set DMTR_API_KEY in the environment for a hosted server; keep keys out of files.
    if (builder.Configuration["DMTR_API_KEY"] is { } apiKey) o.Headers["dmtr-api-key"] = apiKey;
});
builder.Services.AddHealthChecks().AddCheck<UtxorpcHealthCheck>("utxorpc", timeout: TimeSpan.FromSeconds(5));
WebApplication app = builder.Build();
app.MapHealthChecks("/health");

// GET /tip
app.MapGet("/tip", async (UtxorpcClient u5c, CancellationToken ct) =>
{
    Beta.Sync.BlockRef tip = (await u5c.V1Beta.Sync.ReadTipAsync(ct)).Tip;
    return Results.Ok(new { tip.Slot, Hash = Convert.ToHexString(tip.Hash.Span) });
});

// GET /utxos/{address as hex}: streams the outputs as they arrive, page after page.
app.MapGet("/utxos/{address}", (string address, UtxorpcClient u5c, CancellationToken ct) =>
    u5c.V1Beta.Query.SearchUtxosByAddressAsync(Convert.FromHexString(address), cancellationToken: ct)
        .Select(output => new { Reference = $"{Convert.ToHexString(output.TxoRef.Hash.Span)}#{output.TxoRef.Index}", Bytes = output.NativeBytes.Length }));

app.Run();
