using Grpc.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks tip health results and stateless fallback between protocol versions.</summary>
[Trait("Category", "Unit")]
public sealed class HealthCheckTests : ClientTestBase
{
    /// <summary>Reports the current tip slot and lets cancellation propagate.</summary>
    [Fact]
    public async Task Healthy()
    {
        Assert.Throws<ArgumentNullException>(() => new UtxorpcHealthCheck(null!));
        using UtxorpcClient client = new(server.Url);
        UtxorpcHealthCheck check = new(client);
        HealthCheckResult result = await check.CheckHealthAsync(new(), cancellation.Token);
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("tip slot 42", result.Description);
        Assert.Null(result.Exception);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckHealthAsync(new(), cancelled.Token));
    }

    /// <summary>Uses the registration's failure status and attaches the error from either protocol version.</summary>
    [Fact]
    public async Task Unhealthy()
    {
        server.State.FailWith = StatusCode.Unavailable;
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1 });
        UtxorpcHealthCheck check = new(client);
        HealthCheckContext context = new() { Registration = new("utxorpc", check, HealthStatus.Degraded, null) };
        HealthCheckResult result = await check.CheckHealthAsync(context, cancellation.Token);
        AssertFailure(result);
        Assert.Equal(new[] { "/utxorpc.v1beta.sync.SyncService/ReadTip" }, server.State.Calls.Select(call => call.Method));
        server.State.Calls.Clear();
        server.State.FailWith = StatusCode.Unimplemented;
        server.State.FailOnly = "v1beta";
        server.State.FailV1AlphaWith = StatusCode.Unavailable;
        AssertFailure(await check.CheckHealthAsync(context, cancellation.Token));
        Assert.Equal(new[] { "/utxorpc.v1beta.sync.SyncService/ReadTip", "/utxorpc.v1alpha.sync.SyncService/ReadTip" }, server.State.Calls.Select(call => call.Method));

        static void AssertFailure(HealthCheckResult result)
        {
            UtxorpcException error = Assert.IsType<UtxorpcException>(result.Exception);
            Assert.Equal(StatusCode.Unavailable, error.StatusCode);
            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Equal(error.Message, result.Description);
        }
    }

    /// <summary>Tries v1beta on every check, falling back when that version is unsupported.</summary>
    [Fact]
    public async Task FallsBackToV1AlphaWhenV1BetaIsMissing()
    {
        server.State.FailWith = StatusCode.Unimplemented;
        server.State.FailOnly = "v1beta";
        using UtxorpcClient client = new(server.Url);
        UtxorpcHealthCheck check = new(client);
        for (int run = 0; run < 2; run++)
        {
            HealthCheckResult result = await check.CheckHealthAsync(new(), cancellation.Token);
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Equal("tip slot 42", result.Description);
        }
        Assert.Equal(new[] { "/utxorpc.v1beta.sync.SyncService/ReadTip", "/utxorpc.v1alpha.sync.SyncService/ReadTip", "/utxorpc.v1beta.sync.SyncService/ReadTip", "/utxorpc.v1alpha.sync.SyncService/ReadTip" }, server.State.Calls.Select(call => call.Method));
    }
}
