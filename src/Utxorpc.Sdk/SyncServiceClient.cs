using Grpc.Net.Client;
using Utxorpc.V1alpha.Sync;
using Utxorpc.Sdk.Models;
using Block = Utxorpc.Sdk.Models.Block;
using Grpc.Core;
using Utxorpc.Sdk.Utils;
using BlockRef = Utxorpc.Sdk.Models.BlockRef;

namespace Utxorpc.Sdk;

public class SyncServiceClient
{
    private readonly SyncService.SyncServiceClient _client;

    public SyncServiceClient(string url, IDictionary<string, string>? headers = null)
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
        _client = new SyncService.SyncServiceClient(channel);
    }

    public async Task<Block?> FetchBlockAsync(BlockRef blockRef)
    {
        FetchBlockRequest? request = new()
        {
            Ref = { DataUtils.ToSyncBlockRef(blockRef) }
        };

        FetchBlockResponse? response = await _client.FetchBlockAsync(request);
        AnyChainBlock? anyChainBlock = response.Block.FirstOrDefault();
        return DataUtils.FromAnyChainBlock(anyChainBlock);
    }

    public async IAsyncEnumerable<NextResponse> FollowTipAsync(BlockRef blockRef)
    {
        FollowTipRequest? request = new()
        {
            Intersect = { DataUtils.ToSyncBlockRef(blockRef) }
        };

        using AsyncServerStreamingCall<FollowTipResponse>? call = _client.FollowTip(request);
        await foreach (FollowTipResponse? response in call.ResponseStream.ReadAllAsync())
        {
            switch (response.ActionCase)
            {
                case FollowTipResponse.ActionOneofCase.Apply:
                    Block? applyBlock = DataUtils.FromAnyChainBlock(response.Apply);
                    if (applyBlock is not null)
                    {
                        yield return DataUtils.CreateApplyResponse(applyBlock);
                    }
                    break;
                case FollowTipResponse.ActionOneofCase.Undo:
                    Block? undoBlock = DataUtils.FromAnyChainBlock(response.Undo);
                    if (undoBlock is not null)
                    {
                        yield return DataUtils.CreateUndoResponse(undoBlock);
                    }
                    break;
                case FollowTipResponse.ActionOneofCase.Reset:
                    BlockRef? resetRef = DataUtils.FromSyncBlockRef(response.Reset);
                    yield return DataUtils.CreateResetResponse(resetRef);
                    break;
            }
        }
    }


    // TODO: Implement DumpHistoryAsync 
}