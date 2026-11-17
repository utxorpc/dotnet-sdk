# Changelog

## 2.0.0-alpha

### Added

- Start-up validation of the URL and retry, keep-alive and timeout settings for every `AddUtxorpcClient` registration, naming the client and invalid option.
- `UtxorpcHealthCheck` to read the tip, with v1alpha fallback when v1beta is unsupported.
- Compiler warnings for null requests and bare `new(…)` on generated calls, with the “Use the short method” code fix.
- `UtxorpcClient` with v1alpha and v1beta clients for every RPC.
- Short methods for every RPC, with the same names in both versions.
- `UtxorpcException` for short-method failures, preserving the original gRPC status and detail,
  with `UtxorpcErrorKind` and `IsTransient` to identify temporary failures.
- `OutputReference` and `BlockPoint` inputs, compared by value (their bytes, not the array), and paging across UTxOs and history.
- C# 15 union events from `FollowTipAsync` and `WatchTxAsync`, checked by the compiler.
- Optional field masks on short methods whose requests support them.
- Predicate builders on the Spec's predicate types for every match type, a one-match output builder for an address and an asset together, and certificate pattern builders.
- Several blocks, intersect points or transaction hashes in one short call.
- `DefaultTimeout` for unary calls; server streams never time out by default.
- `AddUtxorpcClient((provider, options) => …)` to configure from other services.
- A null request throws `ArgumentNullException` at once.
- `FollowTipAsync()`, `WatchTxAsync()` and `WatchMempoolAsync()` without arguments.
- XML documentation of every parameter, return value and exception.
- `UtxorpcClientOptions` for connection settings.
- Retry of unary calls that fail with `Unavailable`, except `SubmitTx`.
- Keep-alive pings on connections with open calls.
- `AddUtxorpcClient` for shared and named clients in dependency injection.
- A cancellation token on every call.
- Disposal of the channel and handler the client creates.
- `ReadAllAsync` to read a server stream and end the call with the loop.
- Symbols and Source Link.

### Changed

- Targets net11.0 only. On .NET 10, stay on 1.8.0-alpha or use the Spec package's generated clients over your own `GrpcChannel`.
- Uses one channel for all services and both versions.
- Cancelling with a token throws `OperationCanceledException` on channels the SDK creates. An expired deadline throws `RpcException` with `DeadlineExceeded` on every call, its headers included.
- References Grpc.Net.Client 2.85.0, Google.Protobuf 3.36.2 and Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions 11.0.0 directly.
- Includes package licence and repository metadata.

### Removed

- `QueryServiceClient`, `SubmitServiceClient`, `SyncServiceClient` and `WatchServiceClient` wrappers.
- `Models` and `Utils.DataUtils`.
- See "Migrating from 1.8.0-alpha" in the README for replacements.

### Fixed

- Blocks and FollowTip events without a parsed form are no longer dropped; WatchTx transactions without one no longer throw `InvalidOperationException`.
