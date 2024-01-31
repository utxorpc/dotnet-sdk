using Grpc.Net.Client;
using Utxorpc.Submit.V1;
using Utxorpc.Sync.V1;
using Utxorpc.Watch.V1;

namespace Utxorpc.Sdk;

public class UtxorpcClient(string url)
{
    public ChainSyncService.ChainSyncServiceClient ChainSyncClient => new(GrpcChannel.ForAddress(url));
    public WatchService.WatchServiceClient WatchClient => new(GrpcChannel.ForAddress(url));
    public SubmitService.SubmitServiceClient SubmitClient => new(GrpcChannel.ForAddress(url));
}
