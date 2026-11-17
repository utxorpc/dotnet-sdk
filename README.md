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

## Install

```bash
dotnet add package Utxorpc.Sdk --version 2.0.0-alpha
```

Requires .NET 11: the package targets net11.0 only and depends on Utxorpc.Spec 0.19.2-alpha or newer. On .NET 10, stay on Utxorpc.Sdk 1.8.0-alpha, or reference Utxorpc.Spec 0.19.2-alpha (which targets net10.0) and use its generated clients over your own `GrpcChannel`.

## Quick start

Connect with a header, read the tip on both versions, fetch a block, then follow the tip. `apiKey` is supplied by the caller, not written into source.

```csharp
using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk;
using Utxorpc.Sdk.V1Beta;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

string url = args.Length > 0 ? args[0] : "http://localhost:50051";
string? apiKey = args.Length > 1 ? args[1] : null;
using CancellationTokenSource cancellation = new();
Dictionary<string, string>? headers = apiKey is null ? null : new() { ["dmtr-api-key"] = apiKey };
using UtxorpcClient u5c = new(url, headers);
Beta.Sync.ReadTipResponse tip = await u5c.V1Beta.Sync.ReadTipAsync(cancellation.Token);
Console.WriteLine($"v1beta: {tip.Tip.Slot} {Convert.ToHexString(tip.Tip.Hash.Span)}");
Alpha.Sync.ReadTipResponse oldTip = await u5c.V1Alpha.Sync.ReadTipAsync(cancellation.Token);
Console.WriteLine($"v1alpha: {oldTip.Tip.Slot} {Convert.ToHexString(oldTip.Tip.Hash.Span)}");
Beta.Sync.FetchBlockResponse block = await u5c.V1Beta.Sync.FetchBlockAsync(
    new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), FieldMask.FromString("native_bytes"), cancellation.Token);
Console.WriteLine($"Block bytes: {block.Block[0].NativeBytes.Length}");
int events = 0;
await foreach (FollowTipEvent e in
    u5c.V1Beta.Sync.FollowTipAsync(new BlockPoint(tip.Tip.Slot, tip.Tip.Hash.Memory), cancellationToken: cancellation.Token))
{
    Console.WriteLine(e switch
    {
        BlockApplied a => $"Apply: {a.Block.NativeBytes.Length} bytes",
        BlockUndone u => $"Undo: {u.Block.NativeBytes.Length} bytes",
        Reset r => $"Reset: {r.Point.Slot}",
        null => throw new UnreachableException()
    });
    if (++events == 3)
    {
        break;
    }
}
```

The generated calls take the Spec's requests and are always there:

```csharp
tip = await u5c.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token);
oldTip = await u5c.V1Alpha.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token);
block = await u5c.V1Beta.Sync.FetchBlockAsync(
    new() { Ref = { tip.Tip }, FieldMask = new() { Paths = { "native_bytes" } } },
    cancellationToken: cancellation.Token);
events = 0;
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

`SearchAllUtxosAsync` and `SearchUtxosByAddressAsync` read every page: page size 0 lets the server choose; a negative size throws. `DumpAllHistoryAsync` requires a page size of at least 1; Dolos accepts at most 100. On preprod, 10 blocks were about 0.7–0.8 MB with the parsed form, or 0.09 MB with `native_bytes` only. A page is one message: keep it under the 4 MB receive limit, use a mask, or raise the limit (see Message size). The server decides whether to include the block at `from`.

The package includes two compiler warnings:

- `UTXORPC001`: a null or default request argument, such as `ReadTipAsync(default)` or `ReadParamsAsync(null)`, selects the generated call and throws `ArgumentNullException`.
- `UTXORPC002`: a bare `new(…)` with arguments, such as `FetchBlockAsync(new(slot, hash))`, selects the generated call. Write `new BlockPoint(slot, hash)` instead.

The “Use the short method” fix is offered only when the rewrite binds to a short method and adds no compiler errors in the document.
To turn one warning off, set `dotnet_diagnostic.UTXORPC001.severity = none` in `.editorconfig`.
Neither warning covers a token in the mask's place (`FetchBlockAsync(point, ct)`: name it `cancellationToken: ct`) or a bare `new() { … }` for a `Cardano.StateQuery` (write `new Cardano.StateQuery { … }`).

- Byte inputs are wrapped without copying. Do not change their memory until the task completes or enumeration ends.
- Use the raw call for per-call headers and deadlines, or any other request fields.

## Field masks and bytes

Byte fields are `ByteString`; use `.Memory` or `.Span` to read them without copying. The optional `fieldMask` comes before the token. Make a mask once and reuse it; paths are the server's to interpret. Spec messages carry no nullability annotations: `Cardano` (under a mask) and `Header` can be null. For JSON, `Google.Protobuf.JsonFormatter` writes proto3 JSON; System.Text.Json writes byte fields as number arrays. Here, `client` and `point` are a connected client and block point:

```csharp
using Google.Protobuf.WellKnownTypes;
FieldMask bytesOnly = FieldMask.FromString("native_bytes");
await client.V1Beta.Sync.FetchBlockAsync(point, bytesOnly, cancellationToken);
await foreach (Beta.Sync.AnyChainBlock historicalBlock in client.V1Beta.Sync.DumpAllHistoryAsync(
    point, pageSize: 10, fieldMask: bytesOnly, cancellationToken: cancellationToken))
    Console.WriteLine(historicalBlock.NativeBytes.Length);
```

## Cardano inputs

Addresses go in as raw CIP-19 bytes, and amounts come back as the Spec's `BigInt`. For bech32 addresses (`addr1…`, `addr_test1…`), address parts and big-integer amounts, use [Chrysalis](https://www.nuget.org/packages/Chrysalis), the .NET Cardano library.

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

`AllOf`, `AnyOf` and `Not` build the Spec's composite predicates; whether a server evaluates them is the server's choice. Dolos 1.6.3 and Dingo 0.94.1 both evaluate them on watch filters; on `SearchUtxos`, Dolos rejects them with `InvalidArgument` and Dingo returned no outputs for them in our tests, while one match with both parts returned the right output on both.

Byte memory in a predicate is wrapped, not copied; do not change it while the predicate is in use. Empty parts mean no constraint; a wholly empty address or asset throws `ArgumentException`. An empty asset name matches any name; for an exact empty name use the v1beta Spec pattern with `AssetName = ByteString.Empty` (v1alpha cannot express it). Every match type of the Spec has a builder; a hand-built Spec message fits wherever a built one does.

## Both versions

Use v1beta for new code, or v1alpha for servers that have not moved. `u5c.V1Beta` and `u5c.V1Alpha` share one channel; each offers `Query`, `Submit`, `Sync` and `Watch`. `ReadState` exists only in v1beta.

## Connecting

Use `new UtxorpcClient(url, headers)` or the options constructor. Retry and keep-alive are both on by default. To turn both off:

```csharp
UtxorpcClientOptions options = new() { Url = url, MaxAttempts = 1, KeepAliveInterval = Timeout.InfiniteTimeSpan };
UtxorpcClientOptions timed = new() { Url = url, DefaultTimeout = TimeSpan.FromSeconds(30) };
if (apiKey is not null) options.Headers["dmtr-api-key"] = apiKey;
using UtxorpcClient configured = new(options);
```

Construction headers go on every call. In `UtxorpcClientOptions.Headers`, names differing only in case are one header (the last value set wins). Names must be valid gRPC metadata keys. Per-call `Metadata` is added beside the client's headers, not in place of them. An `https://` URL uses TLS.

Unary calls failing with `Unavailable` are tried up to `MaxAttempts` (default 3) times in all. This usually means a lost connection or restarting server, but the server may still have run the call. `SubmitTx` is never retried by the SDK's default policy: check with `WaitForTxAsync` or `ReadTxAsync` before sending it again. Server streams are never retried by that policy: a failure or cancellation is reported at once; resume from the last item yourself. Retry behaviour on a caller-owned channel, or after `ConfigureChannel` changes the policy, is your responsibility.

Default retry costs about 1.2 KB more per unary call (1,139–1,227 bytes measured), nothing per stream message. Turn it off in hot loops such as fetching block after block.

When a connection with an open call receives nothing for `KeepAliveInterval` (default 60 s), a ping checks it; a ping unanswered for 30 s ends the open calls, so a dead connection is noticed after the interval plus 30 s.

`ConfigureChannel` runs last and may change, wrap or replace any channel option. A handler or `HttpClient` you put there stays yours to dispose; the client disposes only the channel and handler it created. Or pass your own `ChannelBase`: you keep and dispose it, and none of these defaults apply. Set `ThrowOperationCanceledOnCancellation = true` on your channel's options to get `OperationCanceledException` on cancellation.

`DefaultTimeout` (none by default) gives every unary call without a deadline of its own that long, counted from the call's start and across its retries; each page of `SearchAllUtxosAsync` and `DumpAllHistoryAsync` is one call. Server streams (`FollowTipAsync`, `WatchTxAsync`, `WatchMempoolAsync`, `WaitForTxAsync` and the generated streams) never get it: end one with your token, or give the generated call a `deadline:`. A `SubmitTx` that times out may still have been accepted: check before sending it again.

### Dependency injection

```csharp
using Microsoft.Extensions.DependencyInjection;
ServiceCollection services = new();
services.AddUtxorpcClient(o => o.Url = url);
services.AddUtxorpcClient("preprod", o => o.Url = url);
services.AddSingleton(new Uri(url));
services.AddUtxorpcClient("fromServices", (provider, o) => o.Url = provider.GetRequiredService<Uri>().ToString());
using ServiceProvider provider = services.BuildServiceProvider();
UtxorpcClient shared = provider.GetRequiredService<UtxorpcClient>();
UtxorpcClient preprod = provider.GetRequiredKeyedService<UtxorpcClient>("preprod");
```

`AddUtxorpcClient(o => …)` gives one shared client, disposed with the container. Inside the delegate, bind configuration with `GetSection("Utxorpc").Bind(o)`. Use `AddUtxorpcClient("preprod", …)` for a named client and inject it with `[FromKeyedServices("preprod")]`. Keep API keys out of logs and source; use configuration or user secrets.

`AddUtxorpcClient((provider, o) => …)` configures from other services (settings, a handler the container owns and disposes). In a console app, add the `Microsoft.Extensions.DependencyInjection` package for `BuildServiceProvider`.

## Streams and cancellation

Every call takes a `CancellationToken`. `ReadAllAsync()` reads a raw server stream and ends the call when the loop ends or is left. The short stream methods send nothing until the loop starts, and each loop opens its own call: looping the same sequence again starts a new stream.

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

Now, in 2.0.0-alpha, use the `FollowTipEvent` switch expression in Quick start. `BlockApplied.Block` and `BlockUndone.Block` hold blocks; `Reset.Point` holds the reset point. `e.Tip` is FollowTip's tip; `e.Response` is the Spec message. Read `WatchTxEvent` the same way, with `TxApplied.Tx`, `TxUndone.Tx` and `Idle.Point`.

The generated call (`u5c.V1Beta.Sync.FollowTip(new() { … }).ReadAllAsync()`) still yields Spec messages. A message without an action this SDK knows ends the short stream with `InvalidOperationException`.

## Errors and deadlines

Server failures arrive as `Grpc.Core.RpcException` with the status code. A call that must finish in time gets a deadline: set `DefaultTimeout` for every unary call, or pass `deadline:` to a generated call. When it expires, the call throws `RpcException` with `StatusCode.DeadlineExceeded`: unary calls, streams, single reads and `ResponseHeadersAsync`, with or without retry, on any channel. On a channel the SDK creates, cancelling your token first throws `OperationCanceledException` (at the same instant, either may win); on your own channel set `ThrowOperationCanceledOnCancellation = true` for that. The rule follows the client's clock and a server that reports the timeout as `DeadlineExceeded`; any other status a server sends for its own timeout arrives unchanged. Only a call with a deadline pays for it: about 250 B per unary call, 104 B per stream and about 130 B per read that has to wait (Release build, .NET 11):

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
| Depended on Utxorpc.Spec only | Also Grpc.Net.Client 2.85.0 and Google.Protobuf 3.36.2 (the DI abstractions come with .NET 11). |
| Targeted net10.0 | Targets **net11.0 only**. On .NET 10, stay on 1.8.0-alpha, or reference Utxorpc.Spec 0.19.2-alpha (it targets net10.0) and use its generated clients over your own `GrpcChannel`. |
| A call failing with `Unavailable` failed at once; no keep-alive | Retried up to 3 attempts in all (the default policy excludes `SubmitTx` and server streams); a ping after 60 s without incoming data while a call is open; connect timeout 15 s. `MaxAttempts = 1` and `KeepAliveInterval = Timeout.InfiniteTimeSpan` restore the old behaviour. |

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
