using Grpc.Net.Client;
using Utxorpc.V1alpha.Cardano;
using Utxorpc.V1alpha.Query;
using Utxorpc.V1alpha.Submit;
using Utxorpc.V1alpha.Sync;
using Utxorpc.V1alpha.Watch;

namespace Utxorpc.Sdk;

public class UtxorpcClient(string url)
{
    public SyncService.SyncServiceClient SyncClient => new(GrpcChannel.ForAddress(url));
    public WatchService.WatchServiceClient WatchClient => new(GrpcChannel.ForAddress(url));
    public SubmitService.SubmitServiceClient SubmitClient => new(GrpcChannel.ForAddress(url));
    public QueryService.QueryServiceClient QueryClient => new(GrpcChannel.ForAddress(url));
}
