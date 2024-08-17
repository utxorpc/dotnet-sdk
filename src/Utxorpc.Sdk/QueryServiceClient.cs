using Grpc.Net.Client;
using Utxorpc.V1alpha.Query;

namespace Utxorpc.Sdk;

public class QueryServiceClient(string url)
{
    private readonly QueryService.QueryServiceClient _client = new(GrpcChannel.ForAddress(url));

    // TODO: Implement ReadData, ReadParams, ReadUtxos, SearchUtxos methods
}