using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using Utxorpc.Sdk.Utils;
using Utxorpc.V1alpha.Cardano;
using Utxorpc.V1alpha.Watch;

namespace Utxorpc.Sdk;

public class WatchServiceClient
{
    private readonly WatchService.WatchServiceClient _client;

    public WatchServiceClient(string url, IDictionary<string, string>? headers = null)
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
        _client = new WatchService.WatchServiceClient(channel);
    }


    public async IAsyncEnumerable<Models.WatchTxResponse> WatchTxAsync(
        Predicate predicate,
        Models.BlockRef[]? intersect = null,
        FieldMask? fieldMask = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WatchTxRequest request = new()
        {
            Predicate = new()
            {
                Match = new()
                {
                    Cardano = new()
                }
            }
        };

        // Convert SDK predicate to protobuf predicate
        switch (predicate)
        {
            case AddressPredicate addressPredicate:
                var addressPattern = new AddressPattern();
                
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
                    default:
                        addressPattern.ExactAddress = ByteString.CopyFrom(addressPredicate.Address);
                        break;
                }
                request.Predicate.Match.Cardano.HasAddress = addressPattern;
                break;

            case AssetPredicate assetPredicate:
                var assetPattern = new AssetPattern();
                
                switch (assetPredicate.AssetSearch)
                {
                    case AssetSearchType.PolicyId:
                        assetPattern.PolicyId = ByteString.CopyFrom(assetPredicate.Asset);
                        break;
                    case AssetSearchType.AssetName:
                        assetPattern.AssetName = ByteString.CopyFrom(assetPredicate.Asset);
                        break;
                    default:
                        assetPattern.AssetName = ByteString.CopyFrom(assetPredicate.Asset);
                        break;
                }
                request.Predicate.Match.Cardano.MovesAsset = assetPattern;
                break;

            default:
                throw new ArgumentException($"Unsupported predicate type: {predicate.GetType().Name}");
        }

        // Add intersect block references if provided
        if (intersect != null)
        {
            foreach (var blockRef in intersect)
            {
                request.Intersect.Add(DataUtils.ToWatchBlockRef(blockRef));
            }
        }

        if (fieldMask != null)
        {
            request.FieldMask = fieldMask;
        }

        using AsyncServerStreamingCall<V1alpha.Watch.WatchTxResponse>? call = _client.WatchTx(request, cancellationToken: cancellationToken);
        await foreach (V1alpha.Watch.WatchTxResponse? response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return DataUtils.FromSpecWatchTxResponse(response);
        }
    }
}