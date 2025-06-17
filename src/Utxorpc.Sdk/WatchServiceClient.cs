using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Utils;
using Utxorpc.V1alpha.Watch;
using BlockRef = Utxorpc.Sdk.Models.BlockRef;
using SpecWatch = Utxorpc.V1alpha.Watch;
using WatchTxResponse = Utxorpc.Sdk.Models.WatchTxResponse;
namespace Utxorpc.Sdk;

public class WatchServiceClient
{
    private readonly WatchService.WatchServiceClient _client;

    public WatchServiceClient(string url, IDictionary<string, string>? headers = null)
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
        _client = new WatchService.WatchServiceClient(channel);
    }


    public async IAsyncEnumerable<WatchTxResponse> WatchTxAsync(
        Predicate predicate,
        BlockRef[]? intersect = null,
        FieldMask? fieldMask = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WatchTxRequest request = new()
        {
            Predicate = predicate.ToWatchTxPredicate()
        };
        if (intersect != null)
        {
            foreach (BlockRef blockRef in intersect)
            {
                request.Intersect.Add(DataUtils.ToWatchBlockRef(blockRef));
            }
        }

        if (fieldMask != null)
        {
            request.FieldMask = fieldMask;
        }

        using AsyncServerStreamingCall<SpecWatch.WatchTxResponse>? call = _client.WatchTx(request, cancellationToken: cancellationToken);
        await foreach (SpecWatch.WatchTxResponse? response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return DataUtils.FromSpecWatchTxResponse(response);
        }
    }
}