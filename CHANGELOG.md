# Changelog

## 2.0.0-alpha

### Added

- Example programs under `examples/`: query, sync, submit, dependency injection and a worker service.
- `dotnet new utxorpc-worker`, from the Utxorpc.Templates package.
- A package icon.
- A server compatibility table in the README, with dated results for specific server versions checked by the live tests.
- Blazor WebAssembly support through gRPC-Web, using the new Grpc.Net.Client.Web 2.85.0 dependency.
- A browser sample in `examples/Browser`.
- Start-up validation of the URL and retry, keep-alive and timeout settings for every `AddUtxorpcClient` registration, naming the client and invalid option.
- `UtxorpcHealthCheck` to read the tip, with v1alpha fallback when v1beta is unsupported.
- Opt-in `ResumeOnFailure` for FollowTip and WatchTx, with `ResumeAttempts` to limit reconnects and `TimeProvider` for the waits.
- Compiler warnings `UTXORPC001` for null requests and `UTXORPC002` for bare `new(…)` on generated calls, with the “Use the short method” code fix.
- `UtxorpcClient` with `V1AlphaClients` and `V1BetaClients` for every RPC.
- Short methods for every RPC in `V1AlphaQueryExtensions`, `V1AlphaSubmitExtensions`, `V1AlphaSyncExtensions`, `V1AlphaWatchExtensions`, `V1BetaQueryExtensions`, `V1BetaSubmitExtensions`, `V1BetaSyncExtensions` and `V1BetaWatchExtensions`, with the same method names in both versions.
- `UtxorpcException` for short-method failures, preserving the original gRPC status and detail,
  with `UtxorpcErrorKind` and `IsTransient` to identify temporary failures.
- `OutputReference` and `BlockPoint` inputs, compared by value (their bytes, not the array), and paging across UTxOs and history.
- C# 15 union events: `FollowTipEvent` (`BlockApplied`, `BlockUndone`, `Reset`) and `WatchTxEvent` (`TxApplied`, `TxUndone`, `Idle`), in `Utxorpc.Sdk.V1Alpha` and `Utxorpc.Sdk.V1Beta`, checked by the compiler.
- Optional field masks on short methods whose requests support them.
- Predicate builders on the Spec's predicate types for every match type, a one-match output builder for an address and an asset together, and certificate pattern builders in `V1AlphaCardanoExtensions` and `V1BetaCardanoExtensions`.
- Several blocks, intersect points or transaction hashes in one short call.
- `DefaultTimeout` for unary calls; server streams never time out by default.
- `AddUtxorpcClient((provider, options) => …)` to configure from other services.
- A null request throws `ArgumentNullException` at once.
- `FollowTipAsync()`, `WatchTxAsync()` and `WatchMempoolAsync()` without arguments.
- XML documentation of every parameter, return value and exception.
- `UtxorpcClientOptions` for connection settings: `Url`, `Headers` and `ConfigureChannel`.
- `MaxAttempts` for retry of unary calls that fail with `Unavailable`, except `SubmitTx`.
- `KeepAliveInterval` for keep-alive pings on connections with open calls.
- `AddUtxorpcClient`, in `UtxorpcServiceCollectionExtensions`, for shared and named clients in dependency injection.
- A cancellation token on every call.
- Disposal of the channel and handler the client creates.
- `ReadAllAsync`, in `ServerStreamingCallExtensions`, to read a server stream and end the call with the loop.
- Symbols and Source Link.

### Changed

- Targets net11.0 only. On .NET 10, stay on 1.8.0-alpha or use the Spec package's generated clients over your own `GrpcChannel`.
- Uses one channel for all services and both versions.
- Cancelling with a token throws `OperationCanceledException` on channels the SDK creates. An expired deadline throws `RpcException` with `DeadlineExceeded` on every call, its headers included.
- References Utxorpc.Spec 0.19.2-alpha, Grpc.Net.Client 2.85.0, Grpc.Net.Client.Web 2.85.0, Google.Protobuf 3.36.2 and Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions 11.0.0 directly.
- Includes package license and repository metadata.

### Removed

- `QueryServiceClient`, `SubmitServiceClient`, `SyncServiceClient` and `WatchServiceClient` wrappers.
- `Models` and `Utils.DataUtils`.
- See "Migrating from 1.8.0-alpha" in the README for replacements.

### Fixed

- Short stream methods report failed connections as `Kind.Unavailable`, not caller cancellation; FollowTip and WatchTx count failed reconnects toward `ResumeAttempts`.
- A short stream reports a server-sent `Cancelled` or disposal of its SDK-owned client during a pending read as `UtxorpcException` (`Kind.Other`, `StatusCode.Cancelled`) instead of `OperationCanceledException`.
- Blocks and FollowTip events without a parsed form are no longer dropped; WatchTx transactions without one no longer throw `InvalidOperationException`.
- Bare client addresses now prompt you to specify `http://` or `https://`; schemes with no resolver report how to register one.
