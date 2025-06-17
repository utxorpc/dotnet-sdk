using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using Utxorpc.Sdk.Utils;
using Utxorpc.V1alpha.Cardano;
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

    public async Task<Models.SubmitTxResponse> SubmitTxAsync(Models.Tx[] txs)
    {
        SubmitTxRequest request = new();

        foreach (var key in txs)
        {
            var protoRef = new AnyChainTx
            {
                Raw = ByteString.CopyFrom(key.Raw)
            };
            request.Tx.Add(protoRef);
        }

        var response = await _client.SubmitTxAsync(request);
        return DataUtils.FromSpecSubmitTxResponse(response);
    }

    public async IAsyncEnumerable<Models.WatchMempoolResponse> WatchMempoolAsync(Predicate predicate, FieldMask? fieldMask, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WatchMempoolRequest request = new()
        {
            Predicate = new()
            {
                Match = new()
                {
                    Cardano = new()
                }
            }
        };

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

        if (fieldMask != null)
        {
            request.FieldMask = fieldMask;
        }

        using AsyncServerStreamingCall<V1alpha.Submit.WatchMempoolResponse>? call = _client.WatchMempool(request, cancellationToken: cancellationToken);
        await foreach (V1alpha.Submit.WatchMempoolResponse? response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return DataUtils.FromSpecWatchMempoolResponse(response);
        }
    }
    
    public async IAsyncEnumerable<Models.WaitForTxResponse> WaitForTxAsync(Models.TxoRef[] txoRefs, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WaitForTxRequest request = new();
        
        foreach (var txoRef in txoRefs)
        {
            if (txoRef.Hash != null)
            {
                request.Ref.Add(ByteString.CopyFrom(txoRef.Hash));
            }
        }
        
        using AsyncServerStreamingCall<V1alpha.Submit.WaitForTxResponse>? call = _client.WaitForTx(request, cancellationToken: cancellationToken);
        await foreach (V1alpha.Submit.WaitForTxResponse? response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return DataUtils.FromSpecWaitForTxResponse(response);
        }
    }
}