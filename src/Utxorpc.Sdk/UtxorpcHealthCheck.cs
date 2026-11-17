using Grpc.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Utxorpc.Sdk;

/// <summary>Checks connectivity by reading the chain tip, using v1alpha if v1beta is unsupported.</summary>
/// <param name="client">The client used to read the tip.</param>
/// <exception cref="ArgumentNullException">The client is null.</exception>
public sealed class UtxorpcHealthCheck(UtxorpcClient client) : IHealthCheck
{
    private readonly UtxorpcClient client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Reads the tip and reports its slot, or the registration's failure status with the gRPC error.</summary>
    /// <param name="context">The health-check registration and its failure status.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A healthy result with the tip slot, or a failed result with the error.</returns>
    /// <exception cref="OperationCanceledException">The check was cancelled on an SDK-created channel.</exception>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            ulong slot;
            try
            {
                slot = (await client.V1Beta.Sync.ReadTipAsync(cancellationToken).ConfigureAwait(false)).Tip.Slot;
            }
            catch (UtxorpcException exception) when (exception.Kind == UtxorpcErrorKind.Unsupported)
            {
                slot = (await client.V1Alpha.Sync.ReadTipAsync(cancellationToken).ConfigureAwait(false)).Tip.Slot;
            }
            return HealthCheckResult.Healthy($"tip slot {slot}");
        }
        catch (RpcException exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception.Message, exception);
        }
    }
}
