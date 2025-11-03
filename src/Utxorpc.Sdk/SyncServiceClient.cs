using Grpc.Net.Client;
using Utxorpc.V1alpha.Sync;
using Utxorpc.Sdk.Models;
using Block = Utxorpc.Sdk.Models.Block;
using Grpc.Core;
using Utxorpc.Sdk.Utils;
using BlockRef = Utxorpc.Sdk.Models.BlockRef;
using Google.Protobuf.WellKnownTypes;

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

    public async Task<BlockRef?> ReadTipAsync()
    {
        ReadTipRequest request = new();
        ReadTipResponse? response = await _client.ReadTipAsync(request);
        return DataUtils.FromSyncBlockRef(response.Tip);
    }

    public async Task<Block?> FetchBlockAsync(BlockRef blockRef)
        => await FetchBlockAsync(blockRef, fieldMask: null);

    public async Task<Block?> FetchBlockAsync(BlockRef blockRef, FieldMask? fieldMask)
    {
        FetchBlockRequest request = new()
        {
            Ref = { DataUtils.ToSyncBlockRef(blockRef) }
        };

        if (fieldMask != null)
            request.FieldMask = fieldMask;

        FetchBlockResponse? response = await _client.FetchBlockAsync(request);
        AnyChainBlock? anyChainBlock = response.Block.FirstOrDefault();
        return DataUtils.FromAnyChainBlock(anyChainBlock);
    }

    public async Task<IReadOnlyList<Block>> FetchBlockAsync(params BlockRef[] blockRefs)
        => await FetchBlockAsync(blockRefs, fieldMask: null);

    public async Task<IReadOnlyList<Block>> FetchBlockAsync(BlockRef[] blockRefs, FieldMask? fieldMask)
    {
        FetchBlockRequest request = new();
        foreach (var blockRef in blockRefs)
        {
            request.Ref.Add(DataUtils.ToSyncBlockRef(blockRef));
        }

        if (fieldMask != null)
            request.FieldMask = fieldMask;

        FetchBlockResponse? response = await _client.FetchBlockAsync(request);
        var blocks = new List<Block>();
        foreach (var anyChainBlock in response.Block)
        {
            var block = DataUtils.FromAnyChainBlock(anyChainBlock);
            if (block is not null)
                blocks.Add(block);
        }
        return blocks;
    }

    public async Task<Models.DumpHistoryResponse> DumpHistoryAsync(BlockRef? startToken = null, uint maxItems = 100)
        => await DumpHistoryAsync(startToken, maxItems, fieldMask: null);

    public async Task<Models.DumpHistoryResponse> DumpHistoryAsync(BlockRef? startToken, uint maxItems, FieldMask? fieldMask)
    {
        DumpHistoryRequest request = new()
        {
            MaxItems = maxItems
        };

        if (startToken is not null)
        {
            request.StartToken = DataUtils.ToSyncBlockRef(startToken);
        }

        if (fieldMask != null)
            request.FieldMask = fieldMask;

        var response = await _client.DumpHistoryAsync(request);
        
        var blocks = new List<Block>();
        foreach (var anyChainBlock in response.Block)
        {
            var block = DataUtils.FromAnyChainBlock(anyChainBlock);
            if (block is not null)
                blocks.Add(block);
        }

        var nextToken = DataUtils.FromSyncBlockRef(response.NextToken);
        return new Models.DumpHistoryResponse(blocks, nextToken);
    }

    public async IAsyncEnumerable<NextResponse> FollowTipAsync(BlockRef? blockRef = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var response in FollowTipAsync(blockRef, fieldMask: null, cancellationToken))
        {
            yield return response;
        }
    }

    public async IAsyncEnumerable<NextResponse> FollowTipAsync(BlockRef? blockRef, FieldMask? fieldMask, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        FollowTipRequest request = new();

        if (blockRef is not null)
        {
            request.Intersect.Add(DataUtils.ToSyncBlockRef(blockRef));
        }

        if (fieldMask != null)
            request.FieldMask = fieldMask;

        using AsyncServerStreamingCall<FollowTipResponse>? call = _client.FollowTip(request, cancellationToken: cancellationToken);
        await foreach (FollowTipResponse? response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            BlockRef? tip = DataUtils.FromSyncBlockRef(response.Tip);

            switch (response.ActionCase)
            {
                case FollowTipResponse.ActionOneofCase.Apply:
                    Block? applyBlock = DataUtils.FromAnyChainBlock(response.Apply);
                    if (applyBlock is not null)
                    {
                        yield return DataUtils.CreateApplyResponse(applyBlock, tip);
                    }
                    break;
                case FollowTipResponse.ActionOneofCase.Undo:
                    Block? undoBlock = DataUtils.FromAnyChainBlock(response.Undo);
                    if (undoBlock is not null)
                    {
                        yield return DataUtils.CreateUndoResponse(undoBlock, tip);
                    }
                    break;
                case FollowTipResponse.ActionOneofCase.Reset:
                    BlockRef? resetRef = DataUtils.FromSyncBlockRef(response.Reset);
                    if (resetRef != null)
                    {
                        yield return DataUtils.CreateResetResponse(resetRef, tip);
                    }
                    break;
            }
        }
    }
}