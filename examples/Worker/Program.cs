using Utxorpc.Sdk;
using UtxorpcWorker;

// Follows the chain tip using one shared client from configuration.
// Usage: dotnet run -- --Utxorpc:Url=http://localhost:50051
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddUtxorpcClient(o =>
{
    builder.Configuration.GetSection("Utxorpc").Bind(o);
    // Set DMTR_API_KEY in the environment for a hosted server; keep keys out of files.
    if (builder.Configuration["DMTR_API_KEY"] is { } apiKey) o.Headers["dmtr-api-key"] = apiKey;
});
builder.Services.AddHostedService<Worker>();
builder.Build().Run();
