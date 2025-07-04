using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Utils;
using Utxorpc.V1alpha.Query;
using ReadUtxosResponse = Utxorpc.Sdk.Models.ReadUtxosResponse;
using TxoRef = Utxorpc.Sdk.Models.TxoRef;
using SpecTxoRef = Utxorpc.V1alpha.Query.TxoRef;
using SpecReadUtxosResponse = Utxorpc.V1alpha.Query.ReadUtxosResponse;
using SpecSearchUtxosResponse = Utxorpc.V1alpha.Query.SearchUtxosResponse;
using SpecReadParamsResponse = Utxorpc.V1alpha.Query.ReadParamsResponse;
using SearchUtxosResponse = Utxorpc.Sdk.Models.SearchUtxosResponse;
using ReadParamsResponse = Utxorpc.Sdk.Models.ReadParamsResponse;

namespace Utxorpc.Sdk;

public class QueryServiceClient
{
    private readonly QueryService.QueryServiceClient _client;

    public QueryServiceClient(string url, IDictionary<string, string>? headers = null)
    {
        HttpClientHandler httpClientHandler = new();
        HttpClient httpClient = new(httpClientHandler);

        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }

        GrpcChannelOptions channelOptions = new()
        {
            HttpClient = httpClient
        };

        GrpcChannel channel = GrpcChannel.ForAddress(url, channelOptions);
        _client = new QueryService.QueryServiceClient(channel);
    }

    public async Task<ReadUtxosResponse> ReadUtxosAsync(TxoRef[] keys, FieldMask? fieldMask)
    {
        ReadUtxosRequest request = new();

        foreach (TxoRef key in keys)
        {
            if (key.Hash != null && key.Index.HasValue)
            {
                SpecTxoRef protoRef = new()
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

        SpecReadUtxosResponse response = await _client.ReadUtxosAsync(request);
        return DataUtils.FromSpecReadUtxosResponse(response);
    }

    public async Task<SearchUtxosResponse> SearchUtxosAsync(Predicate predicate, uint maxItems, FieldMask? fieldMask, string? start_token = null)
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

        SpecSearchUtxosResponse response = await _client.SearchUtxosAsync(request);
        return DataUtils.FromSpecSearchUtxosResponse(response);
    }


    public async Task<ReadParamsResponse> ReadParamsAsync(FieldMask? fieldMask)
    {
        ReadParamsRequest request = new()
        {
            FieldMask = fieldMask
        };
        SpecReadParamsResponse response = await _client.ReadParamsAsync(request);
        return DataUtils.FromSpecReadParamsResponse(response);
    }
}