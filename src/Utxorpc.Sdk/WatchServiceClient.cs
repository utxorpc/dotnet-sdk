using Grpc.Net.Client;
using Utxorpc.V1alpha.Watch;

namespace Utxorpc.Sdk;

public class WatchServiceClient(string url)
{
    private readonly WatchService.WatchServiceClient _client = new(GrpcChannel.ForAddress(url));

    // TODO: Implement WatchTx method
}