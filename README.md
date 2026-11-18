<div align="center">
  <h1 style="font-size: 3em;">UTxORPC.Sdk | .NET 🚀</h1>
  <h4>A gRPC interface for UTxO Blockchains</h4>
</div>

<div align="center">

  ![Forks](https://img.shields.io/github/forks/utxorpc/dotnet-sdk.svg?style=social) 
  ![Stars](https://img.shields.io/github/stars/utxorpc/dotnet-sdk.svg?style=social) 
  ![Contributors](https://img.shields.io/github/contributors/utxorpc/dotnet-sdk.svg) 
  ![Issues](https://img.shields.io/github/issues/utxorpc/dotnet-sdk.svg) 
  ![Issues Closed](https://img.shields.io/github/issues-closed/utxorpc/dotnet-sdk.svg) 
  <a href="https://www.nuget.org/packages/Utxorpc.Sdk/">
    <img src="https://img.shields.io/nuget/v/Utxorpc.Sdk.svg" alt="NuGet">
  </a>
</div>


Requires .NET 11. Utxorpc.Sdk is a .NET client for UTxO RPC. One `UtxorpcClient` offers the Utxorpc.Spec package's generated clients for v1alpha and v1beta over one channel.

**Contents:** [Install](#install) · [Quick start](#quick-start) · [Examples](#examples) · [Short methods](#short-methods) · [Field masks and bytes](#field-masks-and-bytes) · [Cardano inputs](#cardano-inputs) · [Predicates](#predicates) · [Both versions](#both-versions) · [Connecting](#connecting) · [Streams and cancellation](#streams-and-cancellation) · [Errors](#errors) · [Message size](#message-size) · [Server compatibility](#server-compatibility) · [Migrating from 1.8.0-alpha](#migrating-from-180-alpha) · [Testing your code](#testing-your-code) · [Telemetry](#telemetry) · [Tests](#tests)

## Install

```bash
dotnet add package Utxorpc.Sdk --version 2.0.0-alpha
```

Requires .NET 11: the package targets net11.0 only and depends on Utxorpc.Spec 0.19.2-alpha or newer, Grpc.Net.Client 2.85.0, Grpc.Net.Client.Web 2.85.0, Google.Protobuf 3.36.2 and Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions 11.0.0. ASP.NET Core apps already have the health-check abstractions. On .NET 10, stay on Utxorpc.Sdk 1.8.0-alpha, or reference Utxorpc.Spec 0.19.2-alpha (which targets net10.0) and use its generated clients over your own `GrpcChannel`.

## Quick start

Read the tip, fetch its block, then follow the chain from there. After installing the package in a new console app, replace the contents of `Program.cs` with this (`examples/Sync` shows how to handle Ctrl+C):

```csharp
using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk;
using Utxorpc.Sdk.V1Beta;
using Utxorpc.V1Beta.Sync;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

string url = Environment.GetEnvironmentVariable("UTXORPC_URL") ?? "http://localhost:50051";
string? apiKey = Environment.GetEnvironmentVariable("DMTR_API_KEY");
CancellationToken ct = CancellationToken.None;

Dictionary<string, string>? headers = apiKey is null ? null : new() { ["dmtr-api-key"] = apiKey };
using UtxorpcClient u5c = new(url, headers);
Beta.Sync.BlockRef tip = (await u5c.V1Beta.Sync.ReadTipAsync(ct)).Tip;
Console.WriteLine($"Tip: slot {tip.Slot}, hash {Convert.ToHexString(tip.Hash.Span)}");
BlockPoint point = new(tip.Slot, tip.Hash.Memory);
Beta.Sync.FetchBlockResponse fetched = await u5c.V1Beta.Sync.FetchBlockAsync(point, FieldMask.FromFieldNumbers<AnyChainBlock>(AnyChainBlock.NativeBytesFieldNumber), ct);
Console.WriteLine($"Block bytes: {fetched.Block[0].NativeBytes.Length}");
int events = 0;
await foreach (FollowTipEvent e in u5c.V1Beta.Sync.FollowTipAsync(point, cancellationToken: ct))
{
    Console.WriteLine(e switch
    {
        BlockApplied a => $"Apply: {a.Block.NativeBytes.Length} bytes",
        BlockUndone u => $"Undo: {u.Block.NativeBytes.Length} bytes",
        Reset r => $"Reset: slot {r.Point.Slot}",
        null => throw new UnreachableException()
    });
    if (++events == 5) break;
}
```

To try the generated calls, append this to the program. These calls take the Spec's requests and use the same connected `u5c` and cancellation token `ct`:

```csharp
Beta.Sync.ReadTipResponse response = await u5c.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: ct);
Alpha.Sync.ReadTipResponse oldTip = await u5c.V1Alpha.Sync.ReadTipAsync(new(), cancellationToken: ct);
Beta.Sync.FetchBlockResponse block = await u5c.V1Beta.Sync.FetchBlockAsync(
    new() { Ref = { response.Tip }, FieldMask = new() { Paths = { "native_bytes" } } },
    cancellationToken: ct);
int generatedEvents = 0;
await foreach (Beta.Sync.FollowTipResponse update in
    u5c.V1Beta.Sync.FollowTip(new() { Intersect = { response.Tip } }, cancellationToken: ct).ReadAllAsync())
{
    Console.WriteLine(update.ActionCase);
    if (++generatedEvents == 3)
    {
        break;
    }
}
```

## Examples

Each folder under `examples/` is a program that runs against your server. Query, Submit and Sync read the server's URL from `UTXORPC_URL` (default `http://localhost:50051`); DependencyInjection and Worker read it from the `Utxorpc` section of their `appsettings.json`; Browser takes it in a box on its page. For a hosted server, set `DMTR_API_KEY` in the environment for Query, Submit, Sync, DependencyInjection and Worker; keep keys out of files. Browser is not part of the solution: `dotnet build examples/Browser` builds it by itself.

| Example | Run | Shows |
|---|---|---|
| Query | `dotnet run --project examples/Query -- [address as hex] [policy id as hex]` | Protocol parameters with a field mask, outputs matching an address and optional policy, one output and its transaction |
| Sync | `dotnet run --project examples/Sync` | The tip, a block, and the chain followed from there |
| Submit | `dotnet run --project examples/Submit -- [signed transaction as hex] [--submit]` | Evaluating a transaction; with `--submit`, submitting it and waiting for confirmation |
| DependencyInjection | `dotnet run --project examples/DependencyInjection` | A web API over one shared client from configuration, with `/health` |
| Worker | `dotnet run --project examples/Worker` | A background service that follows the chain; resume is on (turn it off with `--Utxorpc:ResumeOnFailure=false`) |
| Browser | `dotnet run --project examples/Browser` | The tip and incoming blocks in Blazor WebAssembly, with a "No compression" tick box |

The Submit example spends nothing unless you pass a transaction you signed yourself together with `--submit`.

### Worker template

```bash
dotnet new install Utxorpc.Templates::2.0.0-alpha
dotnet new utxorpc-worker --name MyIndexer --url http://localhost:50051
```

This creates the Worker example as your own project, referencing this package.

In .NET SDK 11.0.100, the `::` separator is deprecated in favour of `@`: `dotnet new install Utxorpc.Templates@2.0.0-alpha`.

## Short methods

Use `using Utxorpc.Sdk;`. The short methods are in `V1AlphaQueryExtensions`, `V1AlphaSubmitExtensions`, `V1AlphaSyncExtensions`, `V1AlphaWatchExtensions` and their v1beta counterparts, `V1BetaQueryExtensions`, `V1BetaSubmitExtensions`, `V1BetaSyncExtensions`, `V1BetaWatchExtensions`. Names and parameters match in both versions unless marked. `m` is an optional `FieldMask`; every method ends with an optional `CancellationToken`. Raw calls take the corresponding Spec request.

| Service | Short method | Returns | Raw call |
|---|---|---|---|
| Query | `ReadParamsAsync(m)` | `Task<ReadParamsResponse>` | `ReadParamsAsync(request)` |
| Query | `ReadUtxosAsync(references, m)` | `Task<ReadUtxosResponse>` | `ReadUtxosAsync(request)` |
| Query | `SearchAllUtxosAsync(predicate, pageSize = 0, m)` | `IAsyncEnumerable<AnyUtxoData>` | `SearchUtxosAsync(request)` |
| Query | `SearchUtxosByAddressAsync(address, pageSize = 0, m)` | `IAsyncEnumerable<AnyUtxoData>` | `SearchUtxosAsync(request)` |
| Query | `ReadTxAsync(txHash, m)` | `Task<ReadTxResponse>` | `ReadTxAsync(request)` |
| Query | `ReadStateAsync(Cardano.StateQuery query, m)` (v1beta only) | `Task<ReadStateResponse>` | `ReadStateAsync(request)` |
| Query | `ReadGenesisAsync(m)` | `Task<ReadGenesisResponse>` | `ReadGenesisAsync(request)` |
| Query | `ReadEraSummaryAsync(m)` | `Task<ReadEraSummaryResponse>` | `ReadEraSummaryAsync(request)` |
| Query | `ReadDataAsync(datumHashes, m)` | `Task<ReadDataResponse>` | `ReadDataAsync(request)` |
| Submit | `SubmitTxAsync(tx)` | `Task<SubmitTxResponse>` | `SubmitTxAsync(request)` |
| Submit | `WaitForTxAsync(txHash)` or `(txHashes)` | `IAsyncEnumerable<WaitForTxResponse>` | `WaitForTx(request)` |
| Submit | `EvalTxAsync(tx)` | `Task<EvalTxResponse>` | `EvalTxAsync(request)` |
| Submit | `ReadMempoolAsync()` | `Task<ReadMempoolResponse>` | `ReadMempoolAsync(request)` |
| Submit | `WatchMempoolAsync(predicate = null, m)` | `IAsyncEnumerable<WatchMempoolResponse>` | `WatchMempool(request)` |
| Sync | `ReadTipAsync()` | `Task<ReadTipResponse>` | `ReadTipAsync(request)` |
| Sync | `FetchBlockAsync(point, m)` or `(points, m)` | `Task<FetchBlockResponse>` | `FetchBlockAsync(request)` |
| Sync | `FollowTipAsync(from = null, m)` or `(intersect, m)` | `IAsyncEnumerable<FollowTipEvent>` | `FollowTip(request)` |
| Sync | `DumpAllHistoryAsync(from, pageSize, m)` | `IAsyncEnumerable<AnyChainBlock>` | `DumpHistoryAsync(request)` |
| Watch | `WatchTxAsync(predicate = null, from = null, m)` or `(predicate, intersect, m)` | `IAsyncEnumerable<WatchTxEvent>` | `WatchTx(request)` |

`references` is a `ReadOnlySpan<OutputReference>` (`new OutputReference(txHash, index)`); `point` is a `BlockPoint` (`new BlockPoint(slot, hash)`). Hashes, addresses and transaction bytes are `ReadOnlyMemory<byte>`; `datumHashes` is a `ReadOnlySpan<ReadOnlyMemory<byte>>`. A null predicate in `WatchTxAsync` or `WatchMempoolAsync` sends no filter; null `from` sends no start point. `points` and `intersect` are `ReadOnlySpan<BlockPoint>`, `txHashes` a `ReadOnlySpan<ReadOnlyMemory<byte>>`: write `[a, b]`, or pass an array or a span; values are sent in order, and the server finds the intersection among the points. `BlockPoint` and `OutputReference` compare by value (their bytes, not the array), so they work as `HashSet` and dictionary keys; do not change the bytes while they are keys.

Use `SearchAllUtxosAsync` for a predicate or `SearchUtxosByAddressAsync` to match an exact address. Both read every page: page size 0 lets the server choose; a negative size throws. `DumpAllHistoryAsync` requires a page size of at least 1; see [Server compatibility](#server-compatibility) for tested limits and page sizes. A page is one message. A smaller page or a field mask helps stay under the 4 MB receive limit only if the server honours it. Dolos v1.6.3 honours neither on `SearchUtxos`, so [raise the receive limit](#message-size) if needed. The server decides whether to include the block at `from`.

The package includes two compiler warnings:

- `UTXORPC001`: a null or default request argument, such as `ReadTipAsync(default)` or `ReadParamsAsync(null)`, selects the generated call and throws `ArgumentNullException`.
- `UTXORPC002`: a bare `new(…)` with arguments, such as `FetchBlockAsync(new(slot, hash))`, selects the generated call. Write `new BlockPoint(slot, hash)` instead.

The “Use the short method” fix is offered only when the rewrite binds to a short method and adds no compiler errors in the document.
To turn one warning off, set `dotnet_diagnostic.UTXORPC001.severity = none` in `.editorconfig`.
Neither warning covers a token in the mask's place (`FetchBlockAsync(point, ct)`: name it `cancellationToken: ct`) or a bare `new() { … }` for a `Cardano.StateQuery` (write `new Cardano.StateQuery { … }`).

- Byte inputs are wrapped without copying. Do not change their memory until the task completes or enumeration ends.
- Use the raw call for per-call headers and deadlines, or any other request fields.

## Field masks and bytes

Byte fields are `ByteString`; use `.Memory` or `.Span` to read them without copying. The optional `fieldMask` comes before the token. Make a mask once and reuse it. Spec messages carry no nullability annotations: `Cardano` (under a mask) and `Header` can be null. For JSON, `Google.Protobuf.JsonFormatter` writes proto3 JSON; System.Text.Json writes byte fields as number arrays. Here, `client` and `point` are a connected client and block point:

```csharp
using Google.Protobuf.WellKnownTypes;
FieldMask bytesOnly = FieldMask.FromFieldNumbers<Beta.Sync.AnyChainBlock>(Beta.Sync.AnyChainBlock.NativeBytesFieldNumber);
FieldMask headerOnly = FieldMask.FromString<Beta.Sync.AnyChainBlock>("cardano.header");
await client.V1Beta.Sync.FetchBlockAsync(point, bytesOnly, cancellationToken);
await foreach (Beta.Sync.AnyChainBlock historicalBlock in client.V1Beta.Sync.DumpAllHistoryAsync(
    point, pageSize: 10, fieldMask: bytesOnly, cancellationToken: cancellationToken))
    Console.WriteLine(historicalBlock.NativeBytes.Length);
```

Masks select paths from the message in this table, not from the request. Use the Spec's `snake_case` proto field names (for example, `native_bytes` or `cardano.header`). The typed builders check field numbers or paths against the Spec's descriptors. See [Server compatibility](#server-compatibility) for tested mask support.

For FollowTip with `ResumeOnFailure`, keep `cardano.header` in the mask even if you only read `native_bytes`. The first applied or undone block without a header turns off resuming. The next connection failure is thrown instead of triggering a reconnect.

| Version | Request | Paths select from |
|---|---|---|
| v1alpha | `Query.ReadDataRequest` | `Query.ReadDataResponse` |
| v1alpha | `Query.ReadEraSummaryRequest` | `Query.ReadEraSummaryResponse` |
| v1alpha | `Query.ReadGenesisRequest` | `Query.ReadGenesisResponse` |
| v1alpha | `Query.ReadParamsRequest` | `Query.ReadParamsResponse` |
| v1alpha | `Query.ReadTxRequest` | `Query.ReadTxResponse` |
| v1alpha | `Query.ReadUtxosRequest` | `Query.ReadUtxosResponse` |
| v1alpha | `Query.SearchUtxosRequest` | `Query.SearchUtxosResponse` |
| v1alpha | `Submit.WatchMempoolRequest` | Not named by the Spec |
| v1alpha | `Sync.DumpHistoryRequest` | `Sync.AnyChainBlock` |
| v1alpha | `Sync.FetchBlockRequest` | `Sync.AnyChainBlock` |
| v1alpha | `Sync.FollowTipRequest` | `Sync.AnyChainBlock` |
| v1alpha | `Watch.WatchTxRequest` | Not named by the Spec |
| v1beta | `Query.ReadDataRequest` | `Query.ReadDataResponse` |
| v1beta | `Query.ReadEraSummaryRequest` | `Query.ReadEraSummaryResponse` |
| v1beta | `Query.ReadGenesisRequest` | `Query.ReadGenesisResponse` |
| v1beta | `Query.ReadParamsRequest` | `Query.ReadParamsResponse` |
| v1beta | `Query.ReadStateRequest` | `Query.ReadStateResponse` |
| v1beta | `Query.ReadTxRequest` | `Query.ReadTxResponse` |
| v1beta | `Query.ReadUtxosRequest` | `Query.ReadUtxosResponse` |
| v1beta | `Query.SearchUtxosRequest` | `Query.SearchUtxosResponse` |
| v1beta | `Submit.WatchMempoolRequest` | Not named by the Spec |
| v1beta | `Sync.DumpHistoryRequest` | `Sync.AnyChainBlock` |
| v1beta | `Sync.FetchBlockRequest` | `Sync.AnyChainBlock` |
| v1beta | `Sync.FollowTipRequest` | `Sync.AnyChainBlock` |
| v1beta | `Watch.WatchTxRequest` | Not named by the Spec |

In `ReadUtxosResponse` and `SearchUtxosResponse`, a mask can name `items` only as a whole: a repeated field can only end a path. Google.Protobuf's typed builder rejects `items.txo_ref`. The Spec defines no per-output mask. Dolos v1.6.3 returned whole outputs with every mask tested on these calls; see [Server compatibility](#server-compatibility).

## Cardano inputs

Addresses go in as raw CIP-19 bytes. Use `Chrysalis.Wallet.Models.Addresses.Address` from [Chrysalis 1.7.14-alpha](https://www.nuget.org/packages/Chrysalis/1.7.14-alpha), the .NET Cardano library. For bech32 addresses (`addr1…`, `addr_test1…`), decode with `Address.FromBech32(text).ToBytes()` and encode with `Address.FromBytes(bytes).ToBech32()`. Byron addresses use base58, not bech32; `FromBytes` throws `ArgumentOutOfRangeException` for their bytes. Use `GetPaymentKeyHash()` and `GetStakeKeyHash()` to read the payment and stake credential hashes when present.

To read an amount returned as the Spec's `BigInt`, use .NET's `BigInteger`. Both byte fields are unsigned and big-endian; `big_n_int` stores `-1 - n` for a negative integer `n`. This example uses v1beta; for v1alpha, use `Alpha = Utxorpc.V1Alpha` and replace `Beta` with `Alpha`.

```csharp
using System.Numerics;
using Beta = Utxorpc.V1Beta;

static BigInteger ToBigInteger(Beta.Cardano.BigInt value) => value.BigIntCase switch
{
    Beta.Cardano.BigInt.BigIntOneofCase.Int => value.Int,
    Beta.Cardano.BigInt.BigIntOneofCase.BigUInt => new BigInteger(value.BigUInt.Span, isUnsigned: true, isBigEndian: true),
    Beta.Cardano.BigInt.BigIntOneofCase.BigNInt => -1 - new BigInteger(value.BigNInt.Span, isUnsigned: true, isBigEndian: true),
    _ => throw new ArgumentException("No integer value is set.", nameof(value)),
};

Console.WriteLine(ToBigInteger(new Beta.Cardano.BigInt { Int = 42 }));
```

## Predicates

`SearchAllUtxosAsync` takes a Query `UtxoPredicate`; `WatchTxAsync` takes a Watch `TxPredicate`; `WatchMempoolAsync` takes a Submit `TxPredicate`. Build filters on the Spec's own types. One match holds an address, an asset or both: `UtxoPredicate.Match(paymentPart: …, policyId: …)`.

```csharp
await foreach (Beta.Query.AnyUtxoData output in client.V1Beta.Query.SearchAllUtxosAsync(
    Beta.Query.UtxoPredicate.Match(paymentPart: paymentKeyHash, policyId: policyId), cancellationToken: cancellationToken))
    Console.WriteLine(output.NativeBytes.Length);
```

Outputs: `UtxoPredicate.Address(paymentPart: …)`, `.Asset(policyId: …, assetName: …)`. Transactions: `TxPredicate.HasAddress(…)`, `.MovesAsset(…)`, `.MintsAsset(…)`, `.Consumes(…)` and `.Produces(…)` (address parts, asset parts or both, on one input or output), `.HasCertificate(pattern)`. Certificates: `CertificatePattern.StakeRegistration(…)`, `.StakeDeregistration(…)`, `.StakeDelegation(…)`, `.PoolRegistration(…)`, `.PoolRetirement(…)` (with no argument: every certificate of that kind), `.AnyStakeCredential(hash)`, `.AnyPoolKeyhash(hash)`, `.AnyDrep(hash)`.

To watch for an address plus an asset: `TxPredicate.Produces(…)` or `.Consumes(…)` with both parts for one output, `AllOf(HasAddress(…), MovesAsset(…))` for anywhere in the transaction. For example, watch stake delegations to a pool from one of several intersect points:

```csharp
Beta.Watch.TxPredicate delegations = Beta.Watch.TxPredicate.HasCertificate(
    Beta.Cardano.CertificatePattern.StakeDelegation(poolKeyhash: poolId));
await foreach (WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(delegations, [point, olderPoint], cancellationToken: cancellationToken)) break;
Beta.Watch.TxPredicate touches = Beta.Watch.TxPredicate.Produces(paymentPart: paymentKeyHash, policyId: policyId);
await foreach (WatchTxEvent _ in client.V1Beta.Watch.WatchTxAsync(touches, cancellationToken: cancellationToken)) break;
```

`AllOf`, `AnyOf` and `Not` build the Spec's composite predicates; see [Server compatibility](#server-compatibility) for tested server support.

Byte memory in a predicate is wrapped, not copied; do not change it while the predicate is in use. Empty parts mean no constraint; a wholly empty address or asset throws `ArgumentException`. An empty asset name matches any name; for an exact empty name use the v1beta Spec pattern with `AssetName = ByteString.Empty` (v1alpha cannot express it). Every match type of the Spec has a builder; a hand-built Spec message fits wherever a built one does. Certificate builders are in `V1AlphaCardanoExtensions` and `V1BetaCardanoExtensions`.

## Both versions

Use v1beta for new code, or v1alpha for servers that have not moved. `u5c.V1Beta` (`V1BetaClients`) and `u5c.V1Alpha` (`V1AlphaClients`) share one channel; each offers `Query`, `Submit`, `Sync` and `Watch`. `ReadState` exists only in v1beta.

## Connecting

Use `new UtxorpcClient(url, headers)` or the options constructor. Include the scheme: for example, `http://example.com:50051` for plaintext or `https://example.com:50051` for TLS. A bare host or `host:port` with no matching resolver throws `UriFormatException`; the SDK never selects a transport for you. Other schemes need an address resolver and explicit channel credentials. gRPC includes a `dns` resolver; register other resolvers via `ConfigureChannel`.

Retry and keep-alive (outside a browser) are both on by default. To turn both off:

```csharp
UtxorpcClientOptions options = new() { Url = url, MaxAttempts = 1, KeepAliveInterval = Timeout.InfiniteTimeSpan };
if (apiKey is not null) options.Headers["dmtr-api-key"] = apiKey;
using UtxorpcClient configured = new(options);
```

Construction headers go on every call. In `UtxorpcClientOptions.Headers`, names differing only in case are one header (the last value set wins). Names must be valid gRPC metadata keys. Per-call `Metadata` is added beside the client's headers, not in place of them. An `https://` URL uses TLS.

Unary calls failing with `Unavailable` are tried up to `MaxAttempts` (default 3) times in all. This usually means a lost connection or restarting server, but the server may still have run the call. `SubmitTx` is never retried by the SDK's default policy: check with `WaitForTxAsync` or `ReadTxAsync` before sending it again. Server streams are never retried by that policy. FollowTip and WatchTx can reconnect separately when `ResumeOnFailure` is enabled (see Resuming a broken stream). Retry behaviour on a caller-owned channel, or after `ConfigureChannel` changes the policy, is your responsibility.

Default retry costs about 1.25 KB more per unary call (1,248 and 1,259 bytes measured), nothing per stream message. Turn it off in hot loops such as fetching block after block.

Outside a browser, when a connection with an open call receives nothing for `KeepAliveInterval` (default 60 s), a ping checks it; a ping unanswered for 30 s ends the open calls, so a dead connection is noticed after the interval plus 30 s.

`ConfigureChannel` runs last and may change, wrap or replace any channel option. A handler or `HttpClient` you put there stays yours to dispose; the client disposes only the channel and handler it created. Or pass your own `ChannelBase`: you keep and dispose it, and none of these defaults apply. Set `ThrowOperationCanceledOnCancellation = true` on your channel's options to get `OperationCanceledException` on cancellation.

`DefaultTimeout` (none by default) gives every unary call without a deadline of its own that long, counted from the call's start and across its retries; each page of `SearchAllUtxosAsync` and `DumpAllHistoryAsync` is one call. Server streams (`FollowTipAsync`, `WatchTxAsync`, `WatchMempoolAsync`, `WaitForTxAsync` and the generated streams) never get it: end one with your token, or give the generated call a `deadline:`. A `SubmitTx` that times out may still have been accepted: check before sending it again.

### Dependency injection

In a web app, bind the server settings as in `examples/DependencyInjection`:

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddUtxorpcClient(o =>
{
    builder.Configuration.GetSection("Utxorpc").Bind(o);
    // Set DMTR_API_KEY in the environment for a hosted server; keep keys out of files.
    if (builder.Configuration["DMTR_API_KEY"] is { } apiKey) o.Headers["dmtr-api-key"] = apiKey;
});
```

For named clients or settings from another service:

```csharp
using Microsoft.Extensions.DependencyInjection;
ServiceCollection services = new();
services.AddUtxorpcClient("preprod", o => o.Url = url);
services.AddSingleton(new Uri(url));
services.AddUtxorpcClient("fromServices", (provider, o) => o.Url = provider.GetRequiredService<Uri>().ToString());
using ServiceProvider provider = services.BuildServiceProvider();
UtxorpcClient preprod = provider.GetRequiredKeyedService<UtxorpcClient>("preprod");
```

`AddUtxorpcClient(o => …)`, from `UtxorpcServiceCollectionExtensions`, gives one shared client, disposed with the container. Inside the delegate, bind configuration with `GetSection("Utxorpc").Bind(o)`. Use `AddUtxorpcClient("preprod", …)` for a named client and inject it with `[FromKeyedServices("preprod")]`. Set `DMTR_API_KEY` in the environment; keep keys out of files and logs.

`AddUtxorpcClient((provider, o) => …)` configures from other services (settings, a handler the container owns and disposes). In a console app, add the `Microsoft.Extensions.DependencyInjection` package for `BuildServiceProvider`.

A registration with a missing or invalid `Url`, or out-of-range retry, keep-alive or timeout settings, stops the host at start. For example: `Utxorpc client 'preprod': UtxorpcClientOptions.Url is required. (Parameter 'options')`. With a `ConfigureChannel` hook, an address such as `localhost:50051` or a URL with a custom scheme is checked for a resolver when the client is first resolved, not at host start. Without a host, validation happens on the first resolve. Header names are checked and the channel hook runs when the client is first resolved. Every registration is checked, including one a later registration replaced. Each registration keeps its own options, headers and channel hook. A test host that removes or replaces the client still validates the original registration at start: give it a valid `Url` in the test configuration.

### Health check

In an ASP.NET Core app, register the check beside the shared client:

```csharp
builder.Services.AddHealthChecks().AddCheck<UtxorpcHealthCheck>("utxorpc", timeout: TimeSpan.FromSeconds(5));
```

For a named client, use `Microsoft.Extensions.Diagnostics.HealthChecks` and:

```csharp
builder.Services.AddHealthChecks().Add(new HealthCheckRegistration("preprod",
    provider => new UtxorpcHealthCheck(provider.GetRequiredKeyedService<UtxorpcClient>("preprod")),
    null, null, TimeSpan.FromSeconds(5)));
```

The check reads the v1beta tip, falling back to v1alpha when v1beta is unsupported, and reports `tip slot N` on success. If the tip is missing or the call fails, it reports the registration's failure status (`Unhealthy` without a registration) with a description and any call exception. Every check tries v1beta again. The time limit also covers a server that accepts the connection and never answers.

### In the browser (Blazor WebAssembly)

The client works in Blazor WebAssembly with no setting: it uses binary gRPC-Web there, including streams.
The server must speak gRPC-Web and allow the page's origin; see [Server compatibility](#server-compatibility).
See [Errors](#errors) for how browser failures are reported.

`KeepAliveInterval` and the connect timeout do not apply in a browser, which offers neither.
A deadline or `DefaultTimeout` is the only limit on a unary call that gets no answer.

An https page can call only https servers (localhost excepted).

If a browser cannot read a server's compressed answers (see [Server compatibility](#server-compatibility)), stop the client offering compression:

```csharp
UtxorpcClientOptions browserOptions = new() { Url = url, ConfigureChannel = o => o.CompressionProviders = [] };
```

Any API key a page sends is visible to the page's users.

Hosted endpoints that need an API key in a header (for example Demeter) were not verified from a browser.

The sample in `examples/Browser` shows the tip and incoming blocks, with a "No compression" tick box.
It is not part of the solution:

```bash
dotnet build examples/Browser
dotnet run --project examples/Browser
```

## Streams and cancellation

Every call takes a `CancellationToken`. `ReadAllAsync()`, from `ServerStreamingCallExtensions`, reads a raw server stream and ends the call when the loop ends or is left. The short stream methods send nothing until the loop starts, and each loop opens its own call: looping the same sequence again starts a new stream.

### Resuming a broken stream

Resuming is off by default. Enable it for FollowTip and WatchTx on a client the SDK connects:

```csharp
UtxorpcClientOptions resuming = new()
{
    Url = url,
    ResumeOnFailure = true,
    ResumeAttempts = 10,
    TimeProvider = TimeProvider.System
};
using UtxorpcClient resumable = new(resuming);
```

A reconnect does not yield an event a second time; a server's own repeats still pass through (see [Server compatibility](#server-compatibility)). A payment watcher should de-duplicate by transaction hash. If the chain rolled back while disconnected, FollowTip yields one `Reset` to the chosen point; WatchTx yields `TxUndone` events for transactions removed, newest first, before the new events.

Only transient failures resume (see `UtxorpcException.IsTransient`). `ResumeAttempts` counts reconnect attempts in a row, including attempts that fail to connect. The default is 10. The count resets only when a new event arrives, not on a skipped replay. Waits start at 1 second, grow by 1.6 to 120 seconds, and each varies by ±20%, so no wait exceeds 144 seconds. Ten attempts mean about 3 minutes of waiting.

With `ResumeAttempts = null` (also `"ResumeAttempts": null` in configuration), a dead, mistyped or refusing server is retried silently for ever, so cancel the stream or watch the [health check](#health-check).

Resuming needs block headers: a FollowTip field mask must keep `cardano.header`, and the server must send blocks with WatchTx transactions (see [Server compatibility](#server-compatibility)). WatchTx cannot resume inside the first block it yielded from. It keeps the last 10 blocks' yielded events alive to undo them if necessary.

This works only for clients the SDK connects (the options constructor and dependency injection), not `UtxorpcClient(ChannelBase)`. Raw calls, `WaitForTxAsync` and `WatchMempoolAsync` do not resume. `TimeProvider` controls only the resume waits; replace it in tests.

### Stream events

`FollowTipAsync` yields `FollowTipEvent`; `WatchTxAsync` yields `WatchTxEvent`, in `Utxorpc.Sdk.V1Alpha` or `Utxorpc.Sdk.V1Beta`. Read each with a **switch expression** over its three cases. A missing case is compiler warning CS8509; a switch statement is not checked. End the switch with `null => throw new UnreachableException()`: reading a case with `?.` inside an arm otherwise draws CS8655 ("the pattern 'null' is not covered"), and silencing that with `_ =>` would turn the CS8509 check off. Reading the cases allocates nothing.

Before, in 1.8.0-alpha:

```
await foreach (NextResponse r in syncClient.FollowTipAsync(blockRef, cancellationToken: ct))
    switch (r.Action)
    {
        case NextResponseAction.Apply: Save(r.AppliedBlock!.NativeBytes!); break;
        case NextResponseAction.Undo: Remove(r.UndoneBlock!.Slot); break;
        case NextResponseAction.Reset: Rewind(r.ResetRef!.Slot); break;
    }
```

Now, use the `FollowTipEvent` switch expression in Quick start. `BlockApplied.Block` and `BlockUndone.Block` hold blocks; `Reset.Point` holds the reset point. `e.Tip` is FollowTip's tip; `e.Response` is the Spec message. Read `WatchTxEvent` the same way, with `TxApplied.Tx`, `TxUndone.Tx` and `Idle.Point`.

The generated call (`u5c.V1Beta.Sync.FollowTip(new() { … }).ReadAllAsync()`) still yields Spec messages. A message without an action this SDK knows ends the short stream with `InvalidOperationException`.

## Errors

See [Server compatibility](#server-compatibility) for tested server support. Short methods throw `UtxorpcException` with a `Kind`, the server's `Detail` and the original gRPC error as `InnerException`:

```csharp
ReadOnlyMemory<byte> tx = signedTx;
try
{
    await client.V1Beta.Submit.SubmitTxAsync(tx, cancellationToken);
}
catch (UtxorpcException e) when (e.Kind == UtxorpcErrorKind.InvalidTx)
{
    Console.WriteLine(e.Detail);
}
```

| Kind | Means | What to do |
|---|---|---|
| `InvalidTx` | A transaction rejection or a server fault. | Read `StatusCode` and `Detail`, and fix a rejected transaction before sending it again. If submission failed with `Unknown`, the server may have failed: check with `ReadTxAsync`, or `WaitForTxAsync` with a time limit, before retrying. |
| `InvalidRequest` | The server rejected the request's content. | Fix the request. |
| `NotFound` | The requested item is not known. | Check the transaction, block or output reference. |
| `Unsupported` | The server does not offer the call. | Use a server that does. |
| `Unavailable` | The server could not be reached or the connection broke. | Try again (SubmitTx: check first, see Connecting); `IsTransient` identifies temporary failures. |
| `Timeout` | The deadline or `DefaultTimeout` passed. | Check the server and choose a suitable deadline. |
| `Other` | Another failure. | Read `StatusCode` and `Detail`. For `ResourceExhausted` on a large response, see [Message size](#message-size). |

`UtxorpcException` is an `RpcException`, so existing catch blocks still match. Raw generated calls keep gRPC's exceptions, with no SDK error typing or resume. A raw stream's `ReadAllAsync()` can throw `TaskCanceledException` if the connection fails. With the SDK's default handler, short stream and unary methods report a failed connection as `UtxorpcException` with `Kind.Unavailable`.

On a channel the SDK creates, cancelling your token throws `OperationCanceledException`. If you pass separate tokens to the short method and to enumeration, the exception may carry a linked token. A caller-supplied channel, or a hook that turns `ThrowOperationCanceledOnCancellation` off, reports gRPC's `Cancelled`, typed `Other` by short methods. A short stream also reports a server-sent `Cancelled` or disposing the client while a read is pending on a channel the SDK creates as `UtxorpcException` with `Kind.Other`.

On `SubmitTxAsync` and `EvalTxAsync` only, an `Unknown` answer maps to `InvalidTx`. `StatusCode` always keeps the original code: `Unknown` may mean the server itself failed, not that the transaction was invalid.

In a browser, a server that cannot be reached or refuses the page's origin is reported by gRPC as `Internal`; the browser does not say which, so `MaxAttempts` does not retry it. Its `Kind` is `Unavailable` and `IsTransient` is true, so FollowTip and WatchTx reconnect when `ResumeOnFailure` is enabled.

For the observed browser failure with compressed gRPC-Web responses and its workaround, see [Server compatibility](#server-compatibility).

A short method that waits for the network allocates 112 B more (Release) for the typed error; an answer already there costs nothing.

`UtxorpcException.IsTransient(error)` accepts typed or raw errors: it is true for `Unavailable` and for a transport failure reported as `Internal`. A message the client cannot read arrives as `Unavailable` from gRPC but is `Kind.Other` and not transient.

The typed error's `Status.DebugException` is the `RpcException` carrying the gRPC status. For an internally cancelled stream read, the SDK may recover that status from the call. The message is "Kind: detail" (or "Kind (StatusCode)" for empty detail). Use `IsTransient` instead of testing `DebugException`.

A call that must finish in time gets a deadline: set `DefaultTimeout` for every unary call, or pass `deadline:` to a generated call. When it expires, the call throws `RpcException` with `StatusCode.DeadlineExceeded`: unary calls, streams, single reads and `ResponseHeadersAsync`, with or without retry, on any channel. Short unary methods report `Kind.Timeout`. On a channel the SDK creates, cancelling your token first throws `OperationCanceledException` (at the same instant, either may win); on your own channel set `ThrowOperationCanceledOnCancellation = true` for that. The rule follows the client's clock and a server that reports the timeout as `DeadlineExceeded`; any other status a server sends for its own timeout arrives unchanged.

With the SDK's default channel settings, a server-sent `DeadlineExceeded` on a short unary or stream call throws `OperationCanceledException` if the SDK observes the failure more than 1 ms before your own deadline (or when no deadline is set), even if your token was not cancelled. Check your token's `IsCancellationRequested`.

Only a call with a deadline pays for it: about 250 B per unary call, 104 B per stream and about 130 B per read that has to wait (Release build, .NET 11):

```csharp
UtxorpcClientOptions timed = new() { Url = url, DefaultTimeout = TimeSpan.FromSeconds(30) };
using UtxorpcClient timedClient = new(timed);
try
{
    Beta.Sync.ReadTipResponse timedTip = await timedClient.V1Beta.Sync.ReadTipAsync(cancellationToken);
    Console.WriteLine(timedTip.Tip.Slot);
}
catch (RpcException exception) when (exception.StatusCode == StatusCode.DeadlineExceeded)
{
    Console.WriteLine("Deadline exceeded.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled.");
}
```

Outside a browser, the SDK's default handler reports `Unavailable` after 15 s per connection attempt if the host does not answer (3 attempts by default for unary calls that can retry). If the connection fails, check that the URL scheme matches the server: use `http://` for plaintext or `https://` for TLS.

With a custom `ConnectCallback` or `HttpClient` set through `ConfigureChannel`, a connect timeout reported by the handler as cancellation gives a short stream `Kind.Other` (`StatusCode.Cancelled`). That error is not transient, so the stream does not resume. On your own channel, the handler's `ConnectTimeout` applies if you set one; otherwise, the system's connect timeout applies (about 2 minutes was seen).

## Message size

The default gRPC receive limit is 4 MB per message. A large `DumpHistory` page or `SearchUtxos` answer can exceed it. If a response exceeds the limit, a short method throws `UtxorpcException` with `Kind.Other` and `StatusCode.ResourceExhausted` ("Received message exceeds the maximum configured message size."). The error is not transient: the same response will fail again. Raise `MaxReceiveMessageSize` on the channel:

```csharp
using UtxorpcClient largerMessages = new(url,
    configureChannel: o => o.MaxReceiveMessageSize = 16 * 1024 * 1024);
```

In the 2026-11-18 preprod check, Dolos v1.6.3 sent all 935 outputs for one script address in one v1beta `SearchUtxos` message (about 13.9 MB), regardless of page size. The 16 MB client above read them.

## Server compatibility

Checked on preprod on 2026-11-18, for the server versions shown, with v1alpha and v1beta alike (`ReadState` is v1beta only). The live tests read this table: a run against a server fails when its results don't match that server's column.

✅ works · ⏳ works but may exceed 15 s · ❌ does not work · — not tested

| Call | Dolos v1.6.3 | Dingo v0.94.1 |
|---|---|---|
| ReadTip | ✅ | ✅ |
| FetchBlock | ✅ | ✅ |
| FetchBlock mask `native_bytes` | ✅ | ❌ |
| DumpHistory | ✅ | ✅ |
| DumpHistory with parsed headers | ✅ | ❌ |
| FollowTip | ✅ | ✅ |
| WatchTx | ✅ | ✅ |
| WatchTx with block headers | ✅ | ❌ |
| WatchTx with HasAddress and a start point opens and cancels | ✅ | ✅ |
| ReadParams | ✅ | ✅ |
| ReadParams mask `values` | ✅ | ❌ |
| ReadGenesis | ✅ | ✅ |
| ReadEraSummary | ✅ | ✅ |
| ReadTx | ✅ | ✅ |
| ReadUtxos | ✅ | ✅ |
| ReadUtxos mask `items` | ❌ | ❌ |
| SearchUtxos by exact address | ✅ | ✅ |
| SearchUtxos by payment part | ✅ | ⏳ |
| SearchUtxos by delegation part | ❌ | ⏳ |
| SearchUtxos by address and asset | ✅ | ✅ |
| SearchUtxos with AllOf | ❌ ¹ | ❌ ¹ |
| SearchUtxos with AnyOf | ❌ ¹ | ❌ ¹ |
| SearchUtxos paging | — | ✅ |
| ReadState | — | ✅ |
| ReadData | — | ✅ |
| SubmitTx rejects an invalid transaction | ✅ | ✅ |
| SubmitTx of a valid transaction | — | — |
| EvalTx | ✅ | ✅ |
| WaitForTx answers for an unknown transaction | ✅ | ❌ |
| WaitForTx for a submitted transaction | — | — |
| ReadMempool | ❌ | ✅ |
| WatchMempool opens and cancels | ✅ | ✅ |
| Other field masks | — | — |
| Other transaction matches | — | — |

- ¹ Put the address and the asset in one match instead.

Other observations as of 2026-11-18 for Dolos v1.6.3 and Dingo v0.94.1 (not checked by the live tests):

Dolos v1.6.3 accepted `DumpHistory` pages of at most 100 blocks. On preprod, 10 blocks were about 0.7–0.8 MB with the parsed form, or 0.09 MB with `native_bytes` only. This version can repeat the tip block, so WatchTx can yield its matching transactions twice.

Dolos v1.6.3 returned a block when `FetchBlock` got its slot with an all-zero hash. Its v1beta `ReadTip` reported height 0 in the tip reference, alongside a nonzero slot and a hash.

The operator configures the history window for Dolos v1.6.3 (`max_history`). On preprod, exact-address searches still returned unspent outputs created before that window, with `BlockRef` null. `ReadUtxosAsync` returned such an output with `BlockRef` null. For a transaction from before that window, `ReadTxAsync` threw `UtxorpcException` with `Kind.NotFound`, the same error as for an unknown hash (tested with the all-zero hash).

In both API versions, Dolos v1.6.3 ignored both the field mask and the page size on `SearchUtxos` and sent every match in one message. The v1beta response size did not change with page size 1 or with either tested mask. Page size 10 still exceeded the 4 MB limit. On v1beta, it ignored the mask on `ReadUtxos` too, and a `ReadTx` mask of `tx.native_bytes` returned an empty response. Masks did trim `FetchBlock`.

In the `FollowTip` sample, Dingo v0.94.1 started with `Apply` and sent no `Reset`; Dolos v1.6.3 sent `Reset` and then applied that tip block.

Dolos v1.6.3 and Dingo v0.94.1 accepted gRPC-Web and allowed cross-origin calls by default. Dingo v0.94.1 compressed responses of 1 KiB or more when the client offered gzip, but its CORS setup hid `Grpc-Encoding`. Browser calls then failed with `Internal` (`Kind.Other`, not transient). To avoid this, disable compression as shown in [In the browser](#in-the-browser-blazor-webassembly).

## Migrating from 1.8.0-alpha

`X` means `client.V1Alpha` or `client.V1Beta`; messages are the Spec's `Utxorpc.V1Alpha.*` or `Utxorpc.V1Beta.*`, and stream events are the SDK's `Utxorpc.Sdk.V1Alpha` or `Utxorpc.Sdk.V1Beta`. `m` is an optional `FieldMask`, and `ct` is a token. See Stream events for the before-and-after code.

| 1.8.0-alpha | Now |
|---|---|
| `new QueryServiceClient(url, headers)`, `SubmitServiceClient`, `SyncServiceClient`, `WatchServiceClient` | One `new UtxorpcClient(url, headers)` (or `new UtxorpcClient(new UtxorpcClientOptions { … })`, or `services.AddUtxorpcClient(…)`); use `X.Query`, `X.Submit`, `X.Sync`, `X.Watch`; dispose it at shutdown. An empty or blank URL throws `ArgumentException`; a missing scheme throws `UriFormatException`. A scheme with no resolver throws `ArgumentException` without a channel hook or `InvalidOperationException` with one. `AddUtxorpcClient` reports URL validation failures as `OptionsValidationException` at host start (or first resolve without a host); with a `ConfigureChannel` hook, an unresolved address like `localhost:50051` fails when the client is first created rather than at host start. |
| `ReadUtxosAsync(TxoRef[] keys, FieldMask?)` | `X.Query.ReadUtxosAsync([new(txHash, index)], m, ct)` |
| `SearchUtxosAsync(Predicate, uint maxItems, FieldMask?, string? start_token)` | `X.Query.SearchAllUtxosAsync(predicate, pageSize, m, ct)` reads every page; one page: the raw `SearchUtxosAsync(new() { … })`, where an empty `NextToken` ends the pages (v1beta: absent or empty, so test `string.IsNullOrEmpty(response.NextToken)`). |
| `ReadParamsAsync(FieldMask?)`, `ReadGenesisAsync`, `ReadEraSummaryAsync` | `X.Query.ReadParamsAsync(m, ct)`, `ReadGenesisAsync(m, ct)`, `ReadEraSummaryAsync(m, ct)`; `Values.Cardano` is a typed `PParams`. |
| `SubmitTxAsync(Tx[] txs)` | `X.Submit.SubmitTxAsync(txBytes, ct)` once per transaction; read `.Ref`. |
| `WatchMempoolAsync(Predicate, FieldMask?, ct)` | `X.Submit.WatchMempoolAsync(TxPredicate.HasAddress(…), m, ct)`; `WatchMempoolAsync()` sends no predicate. |
| `WaitForTxAsync(TxoRef[] txoRefs, ct)` | `X.Submit.WaitForTxAsync(txHash, ct)` or `([hash1, hash2], ct)`; takes transaction hashes. |
| `ReadTipAsync()` returning the SDK's `BlockRef?` | `(await X.Sync.ReadTipAsync(ct)).Tip`; `Hash` is a `ByteString`, not hex. |
| `FetchBlockAsync` (4 overloads) | `X.Sync.FetchBlockAsync(new BlockPoint(slot, hash), m, ct)`; several blocks: `FetchBlockAsync([a, b], m, ct)`; read `.Block[i].NativeBytes` and `.Cardano`. |
| `DumpHistoryAsync` (2 overloads) | `X.Sync.DumpAllHistoryAsync(from, pageSize, m, ct)` yields blocks across pages; one page: the raw `DumpHistoryAsync(new() { … })`. |
| `FollowTipAsync` (2 overloads) returning `NextResponse` | `X.Sync.FollowTipAsync(point, m, ct)` yields `FollowTipEvent` (namespace `Utxorpc.Sdk.V1Alpha` or `.V1Beta`): read it with a switch expression over `BlockApplied` (`.Block`), `BlockUndone` (`.Block`), `Reset` (`.Point`) (see Stream events); `e.Tip` is the tip, `e.Response` the Spec's `FollowTipResponse`; slot and hash of a block: `a.Block.Cardano.Header.Slot`, `.Hash`. Several intersect points: `FollowTipAsync([a, b], m, ct)`; `FollowTipAsync()` follows from the server's tip. |
| `WatchTxAsync(Predicate, BlockRef[]?, FieldMask?, ct)` | `X.Watch.WatchTxAsync(predicate, from, m, ct)` yields `WatchTxEvent` (same namespace): `TxApplied` (`.Tx`), `TxUndone` (`.Tx`), `Idle` (`.Point`); `e.Response` is the Spec's `WatchTxResponse`. Several intersect points: `WatchTxAsync(predicate, [a, b], m, ct)`; `WatchTxAsync()` sends no predicate or intersection. |
| `Models.Block`, `BlockRef`, `ChainPoint`, `TxoRef`, `Tx` | The Spec's `AnyChainBlock`, `BlockRef`, `ChainPoint`, `TxoRef`, `AnyChainTx`; inputs are `BlockPoint` and `OutputReference`. Hex: `Convert.FromHexString`, `Convert.ToHexString(x.Span)`. |
| `Models.AnyUtxoData`, `AnyChainParams`, the response models, `TxInMempool`, `NextResponse` | The Spec's message of the same name; `NextResponse` is `FollowTipEvent` from `FollowTipAsync` (`FollowTipResponse` from the raw call); `Models.WatchTxResponse` is `WatchTxEvent` from `WatchTxAsync` (`WatchTxResponse` from the raw call). Byte fields are `ByteString` (`.Memory`, `.Span`). |
| `Models.Enums.Stage`, `NextResponseAction`, `WatchTxAction` | The Spec's `Stage`; the actions are the event cases (`BlockApplied`, `BlockUndone`, `Reset`; `TxApplied`, `TxUndone`, `Idle`), or `ActionOneofCase` on raw messages. A message without a known action ends the stream with `InvalidOperationException` (1.8.0-alpha threw it for WatchTx and skipped such FollowTip messages). |
| `Predicate`, `MatchPredicate`, `NotPredicate`, `AllOfPredicate`, `AnyOfPredicate`, `AddressPredicate`, `AssetPredicate`, `AddressSearchType`, `AssetSearchType` | `UtxoPredicate.Match(paymentPart: …, policyId: …)`, `.Address(paymentPart: bytes)`, `.Asset(policyId: …, assetName: …)`, `.AllOf(a, b)`, `.AnyOf(a, b)`, `.Not(a)`; for `WatchTxAsync` and `WatchMempoolAsync` `TxPredicate.HasAddress(…)`, `.MovesAsset(…)`; the search type is the named argument. `MatchPredicate`'s callbacks: build the Spec's pattern directly. |
| `Utils.DataUtils` | Removed: nothing is converted any more. |
| Cancelling a stream threw `RpcException` (`Cancelled`) | `OperationCanceledException` on a channel the SDK creates; on your own channel set `ThrowOperationCanceledOnCancellation = true`. |
| Header names were HTTP header names | Valid gRPC metadata keys only (letters, digits, `-`, `_`, `.`), else `ArgumentException`; upper case is lowered; `-bin` is rejected; in `UtxorpcClientOptions.Headers` names that differ only in case are one header; per-call headers are added beside the client's. |
| A block or FollowTip event without a parsed form was dropped; a WatchTx transaction without one threw `InvalidOperationException` | It is delivered; check `ChainCase` before `.Cardano`. |
| Depended on Utxorpc.Spec only | Also Grpc.Net.Client 2.85.0, Grpc.Net.Client.Web 2.85.0, Google.Protobuf 3.36.2 and Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions 11.0.0 (ASP.NET Core apps already have the health-check abstractions; the DI abstractions come with .NET 11). |
| Targeted net10.0 | Targets **net11.0 only**. On .NET 10, stay on 1.8.0-alpha, or reference Utxorpc.Spec 0.19.2-alpha (it targets net10.0) and use its generated clients over your own `GrpcChannel`. |
| A call failing with `Unavailable` failed at once; no keep-alive | Retried up to 3 attempts in all (the default policy excludes `SubmitTx` and server streams); outside a browser, a ping after 60 s without incoming data while a call is open and a connect timeout of 15 s. `MaxAttempts = 1` and `KeepAliveInterval = Timeout.InfiniteTimeSpan` restore the old behaviour. |

Callers on 1.7.0-alpha or older also meet the Spec 0.19 rename `Utxorpc.V1alpha` → `Utxorpc.V1Alpha`.

## Testing your code

Pass a fake `CallInvoker` through a caller-owned `ChannelBase`. Return a failed task inside the unary call, rather than throwing from the invoker: a synchronous throw leaves the generated call before the short method can type the error.

```csharp
using Grpc.Core;
using Utxorpc.Sdk;
using Beta = Utxorpc.V1Beta;

FakeChannel channel = new(new FakeInvoker(request => request is Beta.Sync.ReadTipRequest
    ? new Beta.Sync.ReadTipResponse { Tip = new() { Slot = 42 } } : null));
using UtxorpcClient client = new(channel);
Console.WriteLine((await client.V1Beta.Sync.ReadTipAsync()).Tip.Slot); // 42
try
{
    await client.V1Beta.Query.ReadParamsAsync();
}
catch (UtxorpcException error) when (error.Kind == UtxorpcErrorKind.NotFound)
{
    Console.WriteLine(error.Detail); // not in the fake
}

sealed class FakeChannel(CallInvoker invoker) : ChannelBase("test")
{
    public override CallInvoker CreateCallInvoker() => invoker;
}
sealed class FakeInvoker(Func<object, object?> answer) : CallInvoker
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Task<TResponse> response = answer(request) is TResponse value ? Task.FromResult(value)
            : Task.FromException<TResponse>(new RpcException(new Status(StatusCode.NotFound, "not in the fake")));
        return new AsyncUnaryCall<TResponse>(response, Task.FromResult(Metadata.Empty), () => Status.DefaultSuccess, () => Metadata.Empty, () => { });
    }
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
}
```

## Telemetry

grpc-dotnet emits one span per call from the `Grpc.Net.Client` activity source, with `grpc.method` and `grpc.status_code` tags.
A resumed stream's reconnect is a new call and a new span.
With OpenTelemetry, subscribe with `AddSource("Grpc.Net.Client")` on your tracing builder.
The `Grpc.Net.Client` event source exposes `total-calls`, `current-calls`, `calls-failed`, `calls-deadline-exceeded`, `messages-sent` and `messages-received`.
The SDK adds no telemetry dependency.

## Tests

The build checks SDK API use against the browser (CA1416), not that making a client succeeds there at run time.
No automated browser test runs; before a release, build and run the sample against a server and press Connect.

```bash
dotnet test --filter "Category=Unit"
```

Unit tests need no server. CI and the release workflow also run `./templates/test.sh` to check the worker template and both packages. Live tests check [Server compatibility](#server-compatibility). For live tests, set `UTXORPC_URL` and optional `UTXORPC_HEADERS` (`name=value;name=value`). `UTXORPC_SERVER` is `dolos` by default, or `dingo`:

```bash
dotnet test --filter "Category=Live"
# For Dingo:
dotnet test --filter "Category=Live|Category=LiveDingo"
```

### 🤝 Contributing

Contributions are welcome! Please fork the repository and submit a pull request. For major changes, please open an issue first to discuss what you would like to change.

### 💬 Join the Community

If you want to discuss UTxO RPC or get involved in the community, join the **TxPipe Discord**! There's a dedicated channel for UTxO RPC where you can connect with other developers, share ideas, and get support.

👉 [Join the TxPipe Discord here!](https://discord.gg/nbkJdPnKHm) 💬

Icon: adapted from the UTxO RPC logo, [utxorpc/docs](https://github.com/utxorpc/docs) `public/logo-dark.svg`, Apache-2.0.
