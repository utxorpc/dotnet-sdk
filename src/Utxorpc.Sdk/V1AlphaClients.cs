using Grpc.Core;
using Utxorpc.Sdk.Internal;
using Spec = Utxorpc.V1Alpha;

namespace Utxorpc.Sdk;

/// <summary>Groups the v1alpha generated service clients.</summary>
public sealed class V1AlphaClients
{
    internal V1AlphaClients(CallInvoker invoker, ResumeSettings? resume)
    {
        Query = new(invoker);
        Submit = new(invoker);
        Sync = new(invoker);
        Watch = new(invoker);
        if (resume is not null)
        {
            StreamResume.Enable(Sync, resume);
            StreamResume.Enable(Watch, resume);
        }
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
