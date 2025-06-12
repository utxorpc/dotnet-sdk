using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using Utxorpc.V1alpha.Cardano;
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

    public async Task<ReadUtxosResponse> ReadUtxosAsync(Models.TxoRef[] keys, FieldMask? fieldMask)
    {
        ReadUtxosRequest request = new();

        foreach (var key in keys)
        {
            V1alpha.Query.TxoRef protoRef = new()
            {
                Hash = ByteString.CopyFrom(key.Hash),
                Index = (uint)key.Index
            };
            request.Keys.Add(protoRef);
        }

        if (fieldMask != null)
        {
            request.FieldMask = fieldMask;
        }

        return await _client.ReadUtxosAsync(request);
    }

    public async Task<SearchUtxosResponse> SearchUtxosAsync(Predicate predicate, uint maxItems, FieldMask? fieldMask, string? start_token = null)
    {
        SearchUtxosRequest request = new()
        {
            Predicate = new()
            {
                Match = new()
                {
                    Cardano = new()
                }
            },
            MaxItems = (int)maxItems
        };

        switch (predicate)
        {
            case AddressPredicate addressPredicate:  
                AddressPattern addressPattern = new ();
                switch (addressPredicate.AddressSearch)
                {
                    case AddressSearchType.ExactAddress:
                        addressPattern.ExactAddress = ByteString.CopyFrom(addressPredicate.Address);
                        break;
                    case AddressSearchType.PaymentPart:
                        addressPattern.PaymentPart = ByteString.CopyFrom(addressPredicate.Address);
                        break;
                    case AddressSearchType.DelegationPart:
                        addressPattern.DelegationPart = ByteString.CopyFrom(addressPredicate.Address);
                        break;
                }
                request.Predicate.Match.Cardano.Address = addressPattern;
                break;

            case AssetPredicate assetPredicate:
                AssetPattern assetPattern = new();
                switch (assetPredicate.AssetSearch)
                {
                    case AssetSearchType.PolicyId:
                        assetPattern.PolicyId = ByteString.CopyFrom(assetPredicate.Asset);
                        break;
                    case AssetSearchType.AssetName:
                        assetPattern.AssetName = ByteString.CopyFrom(assetPredicate.Asset);
                        break;
                }
                request.Predicate.Match.Cardano.Asset = assetPattern;
                break;

            default:
                throw new ArgumentException($"Unsupported predicate type: {predicate.GetType().Name}");
        }

        if (fieldMask != null)
        {
            request.FieldMask = fieldMask;
        }

        if (start_token != null)
        {
            request.StartToken = start_token;
        }

        return await _client.SearchUtxosAsync(request);
    }


    public async Task<ReadParamsResponse> ReadParamsAsync(FieldMask? fieldMask)
    {
        ReadParamsRequest request = new()
        {
            FieldMask = fieldMask
        };
        return await _client.ReadParamsAsync(request);
    }
}