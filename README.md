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


Utxorpc.Sdk is a .NET client for UTxO RPC. One `UtxorpcClient` offers the Utxorpc.Spec package's generated clients for v1alpha and v1beta over one channel.

## Install

```bash
dotnet add package Utxorpc.Sdk --version 2.0.0-alpha
```

Requires .NET 11: the package targets net11.0 only and depends on Utxorpc.Spec 0.19.2-alpha or newer. On .NET 10, stay on Utxorpc.Sdk 1.8.0-alpha, or reference Utxorpc.Spec 0.19.2-alpha (which targets net10.0) and use its generated clients over your own `GrpcChannel`.

## Quick start

Connect with a header, read the tip on both versions, fetch a block, then follow the tip. `apiKey` is supplied by the caller, not written into source.

```csharp
using Grpc.Core;
using Utxorpc.Sdk;
using Utxorpc.Sdk.V1Beta;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

string url = args.Length > 0 ? args[0] : "http://localhost:50051";
string? apiKey = args.Length > 1 ? args[1] : null;
using CancellationTokenSource cancellation = new();
Dictionary<string, string>? headers = apiKey is null ? null : new() { ["dmtr-api-key"] = apiKey };
using UtxorpcClient u5c = new(url, headers);
Beta.Sync.ReadTipResponse tip = await u5c.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token);
Console.WriteLine($"v1beta: {tip.Tip.Slot} {Convert.ToHexString(tip.Tip.Hash.Span)}");
Alpha.Sync.ReadTipResponse oldTip = await u5c.V1Alpha.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token);
Console.WriteLine($"v1alpha: {oldTip.Tip.Slot} {Convert.ToHexString(oldTip.Tip.Hash.Span)}");
Beta.Sync.FetchBlockResponse block = await u5c.V1Beta.Sync.FetchBlockAsync(
    new() { Ref = { tip.Tip }, FieldMask = new() { Paths = { "native_bytes" } } },
    cancellationToken: cancellation.Token);
Console.WriteLine($"Block bytes: {block.Block[0].NativeBytes.Length}");
int events = 0;
await foreach (Beta.Sync.FollowTipResponse response in
    u5c.V1Beta.Sync.FollowTip(new() { Intersect = { tip.Tip } }, cancellationToken: cancellation.Token).ReadAllAsync())
{
    Console.WriteLine(response.ActionCase);
    if (++events == 3)
    {
        break;
    }
}
```

The short methods take .NET inputs and return the Spec's messages, except the stream events described below:

```csharp
tip = await u5c.V1Beta.Sync.ReadTipAsync(cancellation.Token);
block = await u5c.V1Beta.Sync.FetchBlockAsync(new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), cancellationToken: cancellation.Token);
await foreach (FollowTipEvent e in
    u5c.V1Beta.Sync.FollowTipAsync(new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), cancellationToken: cancellation.Token))
{
    Console.WriteLine(e switch
    {
        BlockApplied a => $"Apply: {a.Block.NativeBytes.Length} bytes",
        BlockUndone u => $"Undo: {u.Block.NativeBytes.Length} bytes",
        Reset r => $"Reset: {r.Point.Slot}"
    });
}
```

## Short methods

Use `using Utxorpc.Sdk;`. Names and parameters match in both versions unless marked. `m` is an optional `FieldMask`; every method ends with an optional `CancellationToken`. Raw calls take the corresponding Spec request.

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
| Submit | `WaitForTxAsync(txHash)` | `IAsyncEnumerable<WaitForTxResponse>` | `WaitForTx(request)` |
| Submit | `EvalTxAsync(tx)` | `Task<EvalTxResponse>` | `EvalTxAsync(request)` |
| Submit | `ReadMempoolAsync()` | `Task<ReadMempoolResponse>` | `ReadMempoolAsync(request)` |
| Submit | `WatchMempoolAsync(predicate, m)` | `IAsyncEnumerable<WatchMempoolResponse>` | `WatchMempool(request)` |
| Sync | `ReadTipAsync()` | `Task<ReadTipResponse>` | `ReadTipAsync(request)` |
| Sync | `FetchBlockAsync(point, m)` | `Task<FetchBlockResponse>` | `FetchBlockAsync(request)` |
| Sync | `FollowTipAsync(from, m)` | `IAsyncEnumerable<FollowTipEvent>` | `FollowTip(request)` |
| Sync | `DumpAllHistoryAsync(from, pageSize, m)` | `IAsyncEnumerable<AnyChainBlock>` | `DumpHistoryAsync(request)` |
| Watch | `WatchTxAsync(predicate, from, m)` | `IAsyncEnumerable<WatchTxEvent>` | `WatchTx(request)` |

`references` is a `ReadOnlySpan<OutputReference>` (`new OutputReference(txHash, index)`); `point` is a `BlockPoint` (`new BlockPoint(slot, hash)`). Hashes, addresses and transaction bytes are `ReadOnlyMemory<byte>`; `datumHashes` is a `ReadOnlySpan<ReadOnlyMemory<byte>>`. A null predicate in `WatchTxAsync` or `WatchMempoolAsync` sends no filter; null `from` sends no start point.

`SearchAllUtxosAsync` and `SearchUtxosByAddressAsync` read every page: page size 0 lets the server choose; a negative size throws. `DumpAllHistoryAsync` requires a page size of at least 1; Dolos accepts at most 100. On preprod, 10 blocks were about 0.7–0.8 MB with the parsed form, or 0.09 MB with `native_bytes` only. A page is one message: keep it under the 4 MB receive limit, use a mask, or raise the limit (see Message size). The server decides whether to include the block at `from`.

- Write the types in `new BlockPoint(slot, hash)` and `new Cardano.StateQuery { … }`: a bare `new(…)` selects the generated call.
- When skipping a mask, name the token: `cancellationToken: ct`.
- Never pass a literal `null` or bare `default` first to a unary short method: a generated overload may select a null request (including `ReadTipAsync(default)`). Name the input (`fieldMask: null`, `cancellationToken: ct`) or give its type instead.
- Byte inputs are wrapped without copying. Do not change their memory until the task completes or enumeration ends.
- Use the raw call for several blocks or intersect points, per-call headers and deadlines, or any other request fields.

## Field masks and bytes

Byte fields are `ByteString`; use `.Memory` or `.Span` to read them without copying. The optional `fieldMask` comes before the token. Make a mask once and reuse it; paths are the server's to interpret. Here, `client` and `point` are a connected client and block point:

```csharp
using Google.Protobuf.WellKnownTypes;
FieldMask bytesOnly = FieldMask.FromString("native_bytes");
await client.V1Beta.Sync.FetchBlockAsync(point, bytesOnly, cancellationToken);
await foreach (Beta.Sync.AnyChainBlock historicalBlock in client.V1Beta.Sync.DumpAllHistoryAsync(
    point, pageSize: 10, fieldMask: bytesOnly, cancellationToken: cancellationToken))
    Console.WriteLine(historicalBlock.NativeBytes.Length);
```

## Predicates

Build filters on the Spec's own types: `UtxoPredicate.Address(paymentPart: …)`, `.Asset(policyId: …, assetName: …)`, `TxPredicate.HasAddress(…)`, `.MovesAsset(…)`. Combine them with `AllOf`, `AnyOf` and `Not`.

```csharp
await foreach (Beta.Query.AnyUtxoData output in client.V1Beta.Query.SearchAllUtxosAsync(
    Beta.Query.UtxoPredicate.AllOf(Beta.Query.UtxoPredicate.Address(paymentPart: paymentKeyHash),
        Beta.Query.UtxoPredicate.Asset(policyId: policyId)), cancellationToken: cancellationToken))
    Console.WriteLine(output.NativeBytes.Length);
```

`SearchAllUtxosAsync` takes a Query `UtxoPredicate`; `WatchTxAsync` takes a Watch `TxPredicate`; `WatchMempoolAsync` takes a Submit `TxPredicate`. For other patterns, build the Spec message directly. An empty part means "not given": for an asset whose name is empty, build the v1beta pattern with `AssetName = ByteString.Empty`.

## Both versions

Use v1beta for new code, or v1alpha for servers that have not moved. `u5c.V1Beta` and `u5c.V1Alpha` share one channel; each offers `Query`, `Submit`, `Sync` and `Watch`. `ReadState` exists only in v1beta.

## Connecting

Use `new UtxorpcClient(url, headers)` or the options constructor. Retry and keep-alive are both on by default. To turn both off:

```csharp
UtxorpcClientOptions options = new() { Url = url, MaxAttempts = 1, KeepAliveInterval = Timeout.InfiniteTimeSpan };
if (apiKey is not null) options.Headers["dmtr-api-key"] = apiKey;
using UtxorpcClient configured = new(options);
```

Construction headers go on every call. In `UtxorpcClientOptions.Headers`, names differing only in case are one header (the last value set wins). Names must be valid gRPC metadata keys. Per-call `Metadata` is added beside the client's headers, not in place of them. An `https://` URL uses TLS.

Unary calls failing with `Unavailable` are tried up to `MaxAttempts` (default 3) times in all. This usually means a lost connection or restarting server, but the server may still have run the call. `SubmitTx` is never retried: check with `WaitForTxAsync` or `ReadTxAsync` before sending it again. Server streams are never retried: a failure or cancellation is reported at once; resume from the last item yourself.

Default retry costs about 1.2 KB more per unary call (1,139–1,227 bytes measured), nothing per stream message. Turn it off in hot loops such as fetching block after block.

When a connection with an open call receives nothing for `KeepAliveInterval` (default 60 s), a ping checks it.

`ConfigureChannel` runs last and may change, wrap or replace any channel option. A handler or `HttpClient` you put there stays yours to dispose; the client disposes only the channel and handler it created. Or pass your own `ChannelBase`: you keep and dispose it, and none of these defaults apply. Set `ThrowOperationCanceledOnCancellation = true` on your channel's options to get `OperationCanceledException` on cancellation.

### Dependency injection

```csharp
using Microsoft.Extensions.DependencyInjection;
ServiceCollection services = new();
services.AddUtxorpcClient(o => o.Url = url);
services.AddUtxorpcClient("preprod", o => o.Url = url);
using ServiceProvider provider = services.BuildServiceProvider();
UtxorpcClient shared = provider.GetRequiredService<UtxorpcClient>();
UtxorpcClient preprod = provider.GetRequiredKeyedService<UtxorpcClient>("preprod");
```

`AddUtxorpcClient(o => …)` gives one shared client, disposed with the container. Inside the delegate, bind configuration with `GetSection("Utxorpc").Bind(o)`. Use `AddUtxorpcClient("preprod", …)` for a named client and inject it with `[FromKeyedServices("preprod")]`. Keep API keys out of logs and source; use configuration or user secrets.

## Streams and cancellation

Every call takes a `CancellationToken`. `ReadAllAsync()` reads a raw server stream and ends the call when the loop ends or is left. The short stream methods send nothing until the loop starts, and each loop opens its own call: looping the same sequence again starts a new stream.

### Stream events

`FollowTipAsync` yields `FollowTipEvent`; `WatchTxAsync` yields `WatchTxEvent`, in `Utxorpc.Sdk.V1Alpha` or `Utxorpc.Sdk.V1Beta`. Read each with a **switch expression** over its three cases. A missing case is compiler warning CS8509; a switch statement is not checked. Reading the cases allocates nothing.

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

Now, in 2.0.0-alpha, use the `FollowTipEvent` switch expression in Quick start. `BlockApplied.Block` and `BlockUndone.Block` hold blocks; `Reset.Point` holds the reset point. `e.Tip` is FollowTip's tip; `e.Response` is the Spec message. Read `WatchTxEvent` the same way, with `TxApplied.Tx`, `TxUndone.Tx` and `Idle.Point`.

The generated call (`u5c.V1Beta.Sync.FollowTip(new() { … }).ReadAllAsync()`) still yields Spec messages, also for several intersect points. A message without an action this SDK knows ends the short stream with `InvalidOperationException`.

## Errors and deadlines

Server failures arrive as `Grpc.Core.RpcException` with the status code. Pass a deadline or a token with a timeout for calls that must finish in time.

On a channel the SDK creates, cancelling with a token throws `OperationCanceledException`. An expired deadline throws `RpcException` with `StatusCode.DeadlineExceeded` on a unary call while retry is on (the default), and `OperationCanceledException` on a server stream or with `MaxAttempts = 1`:

```csharp
try
{
    Beta.Sync.ReadTipResponse timedTip = await client.V1Beta.Sync.ReadTipAsync(
        new(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: cancellationToken);
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

On a channel the SDK creates, a host that does not answer a connection attempt fails with `Unavailable` after 15 s per attempt (3 attempts by default). On your own channel, the handler's `ConnectTimeout` applies if you set one; otherwise, the system's connect timeout applies (about 2 minutes was seen).

## Message size

The default gRPC receive limit is 4 MB per message. A large `DumpHistory` page or `SearchUtxos` answer can exceed it. Raise it on the channel:

```csharp
using UtxorpcClient largerMessages = new(url,
    configureChannel: o => o.MaxReceiveMessageSize = 16 * 1024 * 1024);
```

## Migrating from 1.8.0-alpha

`X` means `client.V1Alpha` or `client.V1Beta`; messages are the Spec's `Utxorpc.V1Alpha.*` or `Utxorpc.V1Beta.*`, and stream events are the SDK's `Utxorpc.Sdk.V1Alpha` or `Utxorpc.Sdk.V1Beta`. `m` is an optional `FieldMask`, and `ct` is a token. See Stream events for the before-and-after code.

| 1.8.0-alpha | Now |
|---|---|
| `new QueryServiceClient(url, headers)`, `SubmitServiceClient`, `SyncServiceClient`, `WatchServiceClient` | One `new UtxorpcClient(url, headers)` (or `new UtxorpcClient(new UtxorpcClientOptions { … })`, or `services.AddUtxorpcClient(…)`); use `X.Query`, `X.Submit`, `X.Sync`, `X.Watch`; dispose it at shutdown. An empty or blank URL throws `ArgumentException`. |
| `ReadUtxosAsync(TxoRef[] keys, FieldMask?)` | `X.Query.ReadUtxosAsync([new(txHash, index)], m, ct)` |
| `SearchUtxosAsync(Predicate, uint maxItems, FieldMask?, string? start_token)` | `X.Query.SearchAllUtxosAsync(predicate, pageSize, m, ct)` reads every page; one page: the raw `SearchUtxosAsync(new() { … })`, where an empty `NextToken` ends the pages (v1beta: absent or empty, so test `string.IsNullOrEmpty(response.NextToken)`; Dolos sends a present empty one). |
| `ReadParamsAsync(FieldMask?)`, `ReadGenesisAsync`, `ReadEraSummaryAsync` | `X.Query.ReadParamsAsync(m, ct)`, `ReadGenesisAsync(m, ct)`, `ReadEraSummaryAsync(m, ct)`; `Values.Cardano` is a typed `PParams`. |
| `SubmitTxAsync(Tx[] txs)` | `X.Submit.SubmitTxAsync(txBytes, ct)` once per transaction; read `.Ref`. |
| `WatchMempoolAsync(Predicate, FieldMask?, ct)` | `X.Submit.WatchMempoolAsync(TxPredicate.HasAddress(…), m, ct)` |
| `WaitForTxAsync(TxoRef[] txoRefs, ct)` | `X.Submit.WaitForTxAsync(txHash, ct)`; takes a transaction hash. |
| `ReadTipAsync()` returning the SDK's `BlockRef?` | `(await X.Sync.ReadTipAsync(ct)).Tip`; `Hash` is a `ByteString`, not hex. |
| `FetchBlockAsync` (4 overloads) | `X.Sync.FetchBlockAsync(new BlockPoint(slot, hash), m, ct)`; several blocks: the raw call with several `Ref`s; read `.Block[i].NativeBytes` and `.Cardano`. |
| `DumpHistoryAsync` (2 overloads) | `X.Sync.DumpAllHistoryAsync(from, pageSize, m, ct)` yields blocks across pages; one page: the raw `DumpHistoryAsync(new() { … })`. |
| `FollowTipAsync` (2 overloads) returning `NextResponse` | `X.Sync.FollowTipAsync(point, m, ct)` yields `FollowTipEvent` (namespace `Utxorpc.Sdk.V1Alpha` or `.V1Beta`): read it with a switch expression over `BlockApplied` (`.Block`), `BlockUndone` (`.Block`), `Reset` (`.Point`) (see Stream events); `e.Tip` is the tip, `e.Response` the Spec's `FollowTipResponse`; slot and hash of a block: `a.Block.Cardano.Header.Slot`, `.Hash`. Several intersect points: the raw `X.Sync.FollowTip(new() { … }).ReadAllAsync()`, which yields `FollowTipResponse`. |
| `WatchTxAsync(Predicate, BlockRef[]?, FieldMask?, ct)` | `X.Watch.WatchTxAsync(predicate, from, m, ct)` yields `WatchTxEvent` (same namespace): `TxApplied` (`.Tx`), `TxUndone` (`.Tx`), `Idle` (`.Point`); `e.Response` is the Spec's `WatchTxResponse`. Several intersect points: the raw call. |
| `Models.Block`, `BlockRef`, `ChainPoint`, `TxoRef`, `Tx` | The Spec's `AnyChainBlock`, `BlockRef`, `ChainPoint`, `TxoRef`, `AnyChainTx`; inputs are `BlockPoint` and `OutputReference`. Hex: `Convert.FromHexString`, `Convert.ToHexString(x.Span)`. |
| `Models.AnyUtxoData`, `AnyChainParams`, the response models, `TxInMempool`, `NextResponse` | The Spec's message of the same name; `NextResponse` is `FollowTipEvent` from `FollowTipAsync` (`FollowTipResponse` from the raw call); `Models.WatchTxResponse` is `WatchTxEvent` from `WatchTxAsync` (`WatchTxResponse` from the raw call). Byte fields are `ByteString` (`.Memory`, `.Span`). |
| `Models.Enums.Stage`, `NextResponseAction`, `WatchTxAction` | The Spec's `Stage`; the actions are the event cases (`BlockApplied`, `BlockUndone`, `Reset`; `TxApplied`, `TxUndone`, `Idle`), or `ActionOneofCase` on raw messages. A message without a known action ends the stream with `InvalidOperationException` (1.8.0-alpha threw it for WatchTx and skipped such FollowTip messages). |
| `Predicate`, `MatchPredicate`, `NotPredicate`, `AllOfPredicate`, `AnyOfPredicate`, `AddressPredicate`, `AssetPredicate`, `AddressSearchType`, `AssetSearchType` | `UtxoPredicate.Address(paymentPart: bytes)`, `.Asset(policyId: …, assetName: …)`, `.AllOf(a, b)`, `.AnyOf(a, b)`, `.Not(a)`; for `WatchTxAsync` and `WatchMempoolAsync` `TxPredicate.HasAddress(…)`, `.MovesAsset(…)`; the search type is the named argument. `MatchPredicate`'s callbacks: build the Spec's pattern directly. |
| `Utils.DataUtils` | Removed: nothing is converted any more. |
| Cancelling a stream threw `RpcException` (`Cancelled`) | `OperationCanceledException` on a channel the SDK creates; on your own channel set `ThrowOperationCanceledOnCancellation = true`. |
| Header names were HTTP header names | Valid gRPC metadata keys only (letters, digits, `-`, `_`, `.`), else `ArgumentException`; upper case is lowered; `-bin` is rejected; in `UtxorpcClientOptions.Headers` names that differ only in case are one header; per-call headers are added beside the client's. |
| A block or FollowTip event without a parsed form was dropped; a WatchTx transaction without one threw `InvalidOperationException` | It is delivered; check `ChainCase` before `.Cardano`. |
| Depended on Utxorpc.Spec only | Also Grpc.Net.Client 2.85.0 and Google.Protobuf 3.36.2 (the DI abstractions come with .NET 11). |
| Targeted net10.0 | Targets **net11.0 only**. On .NET 10, stay on 1.8.0-alpha, or reference Utxorpc.Spec 0.19.2-alpha (it targets net10.0) and use its generated clients over your own `GrpcChannel`. |
| A call failing with `Unavailable` failed at once; no keep-alive | Retried up to 3 attempts in all (never `SubmitTx`, never server streams); a ping after 60 s without incoming data while a call is open; connect timeout 15 s. `MaxAttempts = 1` and `KeepAliveInterval = Timeout.InfiniteTimeSpan` restore the old behaviour. |

Callers on 1.7.0-alpha or older also meet the Spec 0.19 rename `Utxorpc.V1alpha` → `Utxorpc.V1Alpha`.

## Tests

```bash
dotnet test --filter "Category=Unit"
```

Unit tests need no server. For live tests, set `UTXORPC_URL` and optional `UTXORPC_HEADERS` (`name=value;name=value`). `UTXORPC_SERVER` is `dolos` by default, or `dingo`:

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
