using Google.Protobuf;
using Grpc.Net.Client;
using Utxorpc.V1alpha.Query;

namespace Utxorpc.Sdk;

public class QueryServiceClient
{
    private readonly QueryService.QueryServiceClient _client;

    public QueryServiceClient(string url, IDictionary<string, string>? headers = null)
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
        _client = new QueryService.QueryServiceClient(channel);
    }
    
    public async Task<SearchUtxosResponse> SearchUtxosAsync(byte[] address, string? start_token = null)
    {
        SearchUtxosRequest request = new()
        {
            Predicate = new()
            {
                Match = new()
                {
                    Cardano = new()
                    {
                        Address = new ()
                        {
                            ExactAddress = ByteString.CopyFrom(address)
                        }
                    }
                }
            },
        };

        if (start_token != null)
        {
            request.StartToken = start_token;
        }

        return await _client.SearchUtxosAsync(request);
    }
}