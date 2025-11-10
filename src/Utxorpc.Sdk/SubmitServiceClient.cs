using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Utils;
using Utxorpc.V1alpha.Submit;
using SpecSubmitTxResponse = Utxorpc.V1alpha.Submit.SubmitTxResponse;
using SpecWaitForTxResponse = Utxorpc.V1alpha.Submit.WaitForTxResponse;
using SpecWatchMempoolResponse = Utxorpc.V1alpha.Submit.WatchMempoolResponse;
using SubmitTxResponse = Utxorpc.Sdk.Models.SubmitTxResponse;
using WaitForTxResponse = Utxorpc.Sdk.Models.WaitForTxResponse;
using WatchMempoolResponse = Utxorpc.Sdk.Models.WatchMempoolResponse;

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

    public async Task<SubmitTxResponse> SubmitTxAsync(Tx[] txs)
    {
        List<byte[]> refs = [];

        foreach (Tx tx in txs)
        {
            SubmitTxRequest request = new()
            {
                Tx = new AnyChainTx
                {
                    Raw = ByteString.CopyFrom(tx.Raw)
                }
            };

            SpecSubmitTxResponse response = await _client.SubmitTxAsync(request);
            refs.Add(response.Ref.ToByteArray());
        }

        return new SubmitTxResponse(refs);
    }

    public async IAsyncEnumerable<WatchMempoolResponse> WatchMempoolAsync(Predicate predicate, FieldMask? fieldMask, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WatchMempoolRequest request = new()
        {
            Predicate = predicate.ToTxPredicate()
        };

        if (fieldMask != null)
        {
            request.FieldMask = fieldMask;
        }

        using AsyncServerStreamingCall<SpecWatchMempoolResponse>? call = _client.WatchMempool(request, cancellationToken: cancellationToken);
        await foreach (SpecWatchMempoolResponse? response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return DataUtils.FromSpecWatchMempoolResponse(response);
        }
    }
    
    public async IAsyncEnumerable<WaitForTxResponse> WaitForTxAsync(TxoRef[] txoRefs, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WaitForTxRequest request = new();
        
        foreach (TxoRef txoRef in txoRefs)
        {
            if (txoRef.Hash != null)
            {
                request.Ref.Add(ByteString.CopyFrom(txoRef.Hash));
            }
        }
        
        using AsyncServerStreamingCall<SpecWaitForTxResponse>? call = _client.WaitForTx(request, cancellationToken: cancellationToken);
        await foreach (SpecWaitForTxResponse? response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return DataUtils.FromSpecWaitForTxResponse(response);
        }
    }
}