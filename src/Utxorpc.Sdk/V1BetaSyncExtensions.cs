using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Beta.Sync;

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1beta synchronization client.</summary>
public static class V1BetaSyncExtensions
{
    /// <summary>Reads the current chain tip.</summary>
    public static Task<ReadTipResponse> ReadTipAsync(this SyncService.SyncServiceClient client, CancellationToken cancellationToken = default) =>
        client.ReadTipAsync(new ReadTipRequest(), cancellationToken: cancellationToken).ResponseAsync;

    /// <summary>Fetches a block without copying hash memory; it must not change until the call returns.</summary>
    /// <remarks>Write the point as <c>new BlockPoint(slot, hash)</c>; for <c>FetchBlockAsync</c>, a bare <c>new(...)</c> picks the generated call unless the token is passed positionally.</remarks>
    public static Task<FetchBlockResponse> FetchBlockAsync(this SyncService.SyncServiceClient client, BlockPoint point, CancellationToken cancellationToken = default)
    {
        FetchBlockRequest request = new();
        request.Ref.Capacity = 1;
        request.Ref.Add(new BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) });
        return client.FetchBlockAsync(request, cancellationToken: cancellationToken).ResponseAsync;
    }

    /// <summary>Follows the tip; an optional point's hash memory must not change until enumeration ends.</summary>
    /// <remarks>Write the point as <c>new BlockPoint(slot, hash)</c>; <c>null</c> follows from the server's current tip.</remarks>
    public static IAsyncEnumerable<FollowTipResponse> FollowTipAsync(this SyncService.SyncServiceClient client, BlockPoint? from, CancellationToken cancellationToken = default)
    {
        FollowTipRequest request = new();
        if (from is BlockPoint point)
        {
            request.Intersect.Capacity = 1;
            request.Intersect.Add(new BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) });
        }
        return client.FollowTip(request, cancellationToken: cancellationToken).ReadAllAsync(cancellationToken);
    }

    /// <summary>Reads every history page; an optional point's hash memory must not change until enumeration ends.</summary>
    /// <remarks>Page size must be at least 1: Dolos returns no blocks and a token for 0. Each page is one gRPC message, so page size times block size must fit the receive limit (4 MB by default); a native_bytes mask makes blocks about eight times smaller. Whether the block at from is included is the server's choice.</remarks>
    public static IAsyncEnumerable<AnyChainBlock> DumpAllHistoryAsync(this SyncService.SyncServiceClient client, BlockPoint? from, int pageSize, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        BlockRef? start = from is BlockPoint point
            ? new BlockRef { Slot = point.Slot, Hash = UnsafeByteOperations.UnsafeWrap(point.Hash) }
            : null;
        return Paging.ReadAllPagesAsync<SyncService.SyncServiceClient, (BlockRef? Start, uint PageSize, FieldMask? Mask), BlockRef, DumpHistoryResponse, AnyChainBlock>(
            client, (start, (uint)pageSize, fieldMask),
            static (syncClient, state, token, ct) => syncClient.DumpHistoryAsync(new DumpHistoryRequest
            {
                StartToken = token ?? state.Start,
                MaxItems = state.PageSize,
                FieldMask = state.Mask
            }, cancellationToken: ct).ResponseAsync,
            static response => response.Block,
            static response => response.NextToken,
            cancellationToken);
    }
}
