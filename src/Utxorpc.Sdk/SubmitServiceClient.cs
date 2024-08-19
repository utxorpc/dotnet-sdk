using Grpc.Net.Client;
using Utxorpc.V1alpha.Submit;

namespace Utxorpc.Sdk;

public class SubmitServiceClient(string url)
{
    private readonly SubmitService.SubmitServiceClient _client = new(GrpcChannel.ForAddress(url));

    // TODO: Implement EvalTx, ReadMempool, SubmitTx, WaitForTx, WatchMempool methods
}