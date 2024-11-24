using Grpc.Net.Client;
using Utxorpc.V1alpha.Submit;

namespace Utxorpc.Sdk;

public class SubmitServiceClient
{
    private readonly SubmitService.SubmitServiceClient _client;

    public SubmitServiceClient(string url, IDictionary<string, string>? headers = null)
    {
        var httpClientHandler = new HttpClientHandler();
        var httpClient = new HttpClient(httpClientHandler);
        
        if (headers != null)
        {
            foreach (var header in headers)
            {
                httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }

        var channelOptions = new GrpcChannelOptions
        {
            HttpClient = httpClient
        };

        var channel = GrpcChannel.ForAddress(url, channelOptions);
        _client = new SubmitService.SubmitServiceClient(channel);
    }
}