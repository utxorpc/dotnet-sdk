using Grpc.Core;
using Spec = Utxorpc.V1Beta;

namespace Utxorpc.Sdk;

/// <summary>Groups the v1beta generated service clients.</summary>
public sealed class V1BetaClients
{
    internal V1BetaClients(CallInvoker invoker)
    {
        Query = new(invoker);
        Submit = new(invoker);
        Sync = new(invoker);
        Watch = new(invoker);
    }

    /// <summary>Gets the generated query client.</summary>
    public Spec.Query.QueryService.QueryServiceClient Query { get; }
    /// <summary>Gets the generated submission client.</summary>
    public Spec.Submit.SubmitService.SubmitServiceClient Submit { get; }
    /// <summary>Gets the generated synchronization client.</summary>
    public Spec.Sync.SyncService.SyncServiceClient Sync { get; }
    /// <summary>Gets the generated watch client.</summary>
    public Spec.Watch.WatchService.WatchServiceClient Watch { get; }
}
