using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using Utxorpc.Sdk.Utils;
using Utxorpc.V1alpha.Cardano;
using Utxorpc.V1alpha.Query;

namespace Utxorpc.Sdk;

public class QueryServiceClient
{
    private readonly QueryService.QueryServiceClient _client;

    public QueryServiceClient(string url, IDictionary<string, string>? headers = null)
    {
        HttpClientHandler httpClientHandler = new HttpClientHandler();
        HttpClient httpClient = new HttpClient(httpClientHandler);

        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }

        GrpcChannelOptions channelOptions = new GrpcChannelOptions
        {
            HttpClient = httpClient
        };

        GrpcChannel channel = GrpcChannel.ForAddress(url, channelOptions);
        _client = new QueryService.QueryServiceClient(channel);
    }

    public async Task<Models.ReadUtxosResponse> ReadUtxosAsync(Models.TxoRef[] keys, FieldMask? fieldMask)
    {
        ReadUtxosRequest request = new();

        foreach (Models.TxoRef key in keys)
        {
            if (key.Hash != null && key.Index.HasValue)
            {
                V1alpha.Query.TxoRef protoRef = new()
                {
                    Hash = ByteString.CopyFrom(key.Hash),
                    Index = (uint)key.Index.Value
                };
                request.Keys.Add(protoRef);
            }
        }

        if (fieldMask != null)
        {
            request.FieldMask = fieldMask;
        }

        V1alpha.Query.ReadUtxosResponse response = await _client.ReadUtxosAsync(request);
        return DataUtils.FromSpecReadUtxosResponse(response);
    }

    public async Task<Models.SearchUtxosResponse> SearchUtxosAsync(Predicate predicate, uint maxItems, FieldMask? fieldMask, string? start_token = null)
    {
        SearchUtxosRequest request = new()
        {
            Predicate = predicate.ToUtxoPredicate(),
            MaxItems = (int)maxItems
        };

        if (fieldMask != null)
        {
            request.FieldMask = fieldMask;
        }

        if (start_token != null)
        {
            request.StartToken = start_token;
        }

        V1alpha.Query.SearchUtxosResponse response = await _client.SearchUtxosAsync(request);
        return DataUtils.FromSpecSearchUtxosResponse(response);
    }


    public async Task<Models.ReadParamsResponse> ReadParamsAsync(FieldMask? fieldMask)
    {
        ReadParamsRequest request = new()
        {
            FieldMask = fieldMask
        };
        V1alpha.Query.ReadParamsResponse response = await _client.ReadParamsAsync(request);
        return DataUtils.FromSpecReadParamsResponse(response);
    }
}