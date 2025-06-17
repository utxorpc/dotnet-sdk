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
            Predicate = predicate.ToWatchTxPredicate()
        };
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