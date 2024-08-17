using Grpc.Net.Client;
using Utxorpc.V1alpha.Query;
using Utxorpc.V1alpha.Submit;
using Utxorpc.V1alpha.Sync;
using Utxorpc.V1alpha.Watch;
using Utxorpc.Sdk.Models;
using Block = Utxorpc.Sdk.Models.Block;
using Grpc.Core;
using Utxorpc.Sdk.Utils;
using BlockRef = Utxorpc.Sdk.Models.BlockRef;

namespace Utxorpc.Sdk;

public class SyncServiceClient(string url)
{
    private readonly SyncService.SyncServiceClient _client = new(GrpcChannel.ForAddress(url));

    public async Task<Block?> FetchBlockAsync(string hash, ulong index)
    {
        FetchBlockRequest? request = new()
        {
            Ref = { DataUtils.ToSyncBlockRef(new BlockRef(hash, index)) }
        };

        FetchBlockResponse? response = await _client.FetchBlockAsync(request);
        AnyChainBlock? anyChainBlock = response.Block.FirstOrDefault();
        return DataUtils.FromAnyChainBlock(anyChainBlock);
    }

    public async IAsyncEnumerable<NextResponse> FollowTipAsync(string hash, ulong index)
    {
        FollowTipRequest? request = new()
        {
            Intersect = { DataUtils.ToSyncBlockRef(new BlockRef(hash, index)) }
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

public class WatchServiceClient(string url)
{
    private readonly WatchService.WatchServiceClient _client = new(GrpcChannel.ForAddress(url));

    // TODO: Implement WatchTx method
}

public class SubmitServiceClient(string url)
{
    private readonly SubmitService.SubmitServiceClient _client = new(GrpcChannel.ForAddress(url));

    // TODO: Implement EvalTx, ReadMempool, SubmitTx, WaitForTx, WatchMempool methods
}

public class QueryServiceClient(string url)
{
    private readonly QueryService.QueryServiceClient _client = new(GrpcChannel.ForAddress(url));

    // TODO: Implement ReadData, ReadParams, ReadUtxos, SearchUtxos methods
}