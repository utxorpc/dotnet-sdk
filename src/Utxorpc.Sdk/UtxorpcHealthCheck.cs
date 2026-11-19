using Grpc.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Utxorpc.Sdk;

/// <summary>Checks connectivity by reading the chain tip, using v1alpha if v1beta is unsupported.</summary>
/// <param name="client">The client used to read the tip.</param>
/// <exception cref="ArgumentNullException">The client is null.</exception>
public sealed class UtxorpcHealthCheck(UtxorpcClient client) : IHealthCheck
{
    private readonly UtxorpcClient client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Reports the tip slot, or a failure if the tip is missing or the call fails.</summary>
    /// <param name="context">The health-check context. Without a registration, the failure status is <see cref="HealthStatus.Unhealthy"/>.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A healthy result with the tip slot, or a failed result with a description and any call exception.</returns>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the check on an SDK-created channel.</exception>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        HealthStatus failureStatus = context.Registration?.FailureStatus ?? HealthStatus.Unhealthy;
        try
        {
            ulong? slot;
            try
            {
                slot = (await client.V1Beta.Sync.ReadTipAsync(cancellationToken).ConfigureAwait(false)).Tip?.Slot;
            }
            catch (UtxorpcException exception) when (exception.Kind == UtxorpcErrorKind.Unsupported)
            {
                slot = (await client.V1Alpha.Sync.ReadTipAsync(cancellationToken).ConfigureAwait(false)).Tip?.Slot;
            }
            return slot is ulong tip
                ? HealthCheckResult.Healthy($"tip slot {tip}")
                : new HealthCheckResult(failureStatus, "The server returned no chain tip.");
        }
        catch (Exception exception) when (exception is RpcException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new HealthCheckResult(failureStatus, exception.Message, exception);
        }
    }
}
