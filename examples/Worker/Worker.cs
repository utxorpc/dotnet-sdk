using System.Diagnostics;
using Utxorpc.Sdk;
using Utxorpc.Sdk.V1Beta;

namespace UtxorpcWorker;

/// <summary>Follows the chain tip and logs every event until the host stops.</summary>
public sealed class Worker(UtxorpcClient u5c, ILogger<Worker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (FollowTipEvent e in u5c.V1Beta.Sync.FollowTipAsync(cancellationToken: stoppingToken))
        {
            logger.LogInformation("{Event}", e switch
            {
                BlockApplied a => $"Apply: {a.Block.NativeBytes.Length} bytes",
                BlockUndone u => $"Undo: {u.Block.NativeBytes.Length} bytes",
                Reset r => $"Reset: {r.Point.Slot}",
                null => throw new UnreachableException()
            });
        }
    }
}
