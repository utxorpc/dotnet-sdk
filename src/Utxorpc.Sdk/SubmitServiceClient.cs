using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Utils;
using Utxorpc.V1alpha.Submit;

namespace Utxorpc.Sdk;

public class SubmitServiceClient
{
    private readonly SubmitService.SubmitServiceClient _client;

    public SubmitServiceClient(string url, IDictionary<string, string>? headers = null)
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
        _client = new SubmitService.SubmitServiceClient(channel);
    }

    public async Task<Models.SubmitTxResponse> SubmitTxAsync(Models.Tx[] txs)
    {
        SubmitTxRequest request = new();

        foreach (Models.Tx key in txs)
        {
            AnyChainTx protoRef = new()
            {
                Raw = ByteString.CopyFrom(key.Raw)
            };
            request.Tx.Add(protoRef);
        }

        V1alpha.Submit.SubmitTxResponse response = await _client.SubmitTxAsync(request);
        return DataUtils.FromSpecSubmitTxResponse(response);
    }

    public async IAsyncEnumerable<Models.WatchMempoolResponse> WatchMempoolAsync(Predicate predicate, FieldMask? fieldMask, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WatchMempoolRequest request = new()
        {
            Predicate = predicate.ToTxPredicate()
        };

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
    
    public async IAsyncEnumerable<Models.WaitForTxResponse> WaitForTxAsync(TxoRef[] txoRefs, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WaitForTxRequest request = new();
        
        foreach (TxoRef txoRef in txoRefs)
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