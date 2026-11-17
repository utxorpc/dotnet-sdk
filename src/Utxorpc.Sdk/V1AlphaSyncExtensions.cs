using System.Runtime.CompilerServices;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Utxorpc.Sdk.Internal;
using Utxorpc.Sdk.V1Alpha;
using Utxorpc.V1Alpha.Sync;
using FollowTipCall = (Utxorpc.V1Alpha.Sync.SyncService.SyncServiceClient Client, Utxorpc.V1Alpha.Sync.FollowTipRequest Request);

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1alpha synchronization client.</summary>
public static class V1AlphaSyncExtensions
{
    /// <include file="Docs.xml" path="docs/member[@name='ReadTipAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    public static Task<ReadTipResponse> ReadTipAsync(this SyncService.SyncServiceClient client, CancellationToken cancellationToken = default) =>
        client.ReadTipAsync(new ReadTipRequest(), cancellationToken: cancellationToken).ResponseAsync;

    /// <include file="Docs.xml" path="docs/member[@name='FetchBlockAsync']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    [OverloadResolutionPriority(1)]
    public static Task<FetchBlockResponse> FetchBlockAsync(this SyncService.SyncServiceClient client, BlockPoint point, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.FetchBlockAsync(new ReadOnlySpan<BlockPoint>(in point), fieldMask, cancellationToken);

    /// <include file="Docs.xml" path="docs/member[@name='FetchBlockAsync(points)']/*"/>
    /// <include file="Docs.xml" path="docs/call/*"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    public static Task<FetchBlockResponse> FetchBlockAsync(this SyncService.SyncServiceClient client, ReadOnlySpan<BlockPoint> points, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        FetchBlockRequest request = new() { FieldMask = fieldMask };
        AddPoints(request.Ref, points);
        return client.FetchBlockAsync(request, cancellationToken: cancellationToken).ResponseAsync;
    }

    /// <include file="Docs.xml" path="docs/member[@name='FollowTipAsync']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='FollowTipAsync']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
    [OverloadResolutionPriority(1)]
    public static IAsyncEnumerable<FollowTipEvent> FollowTipAsync(this SyncService.SyncServiceClient client, BlockPoint? from = null, FieldMask? fieldMask = null, CancellationToken cancellationToken = default) =>
        client.FollowTipAsync(from is BlockPoint point ? new ReadOnlySpan<BlockPoint>(in point) : default, fieldMask, cancellationToken);

    /// <include file="Docs.xml" path="docs/member[@name='FollowTipAsync(intersect)']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='FollowTipAsync(intersect)']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
    public static IAsyncEnumerable<FollowTipEvent> FollowTipAsync(this SyncService.SyncServiceClient client, ReadOnlySpan<BlockPoint> intersect, FieldMask? fieldMask = null, CancellationToken cancellationToken = default)
    {
        FollowTipRequest request = new() { FieldMask = fieldMask };
        AddPoints(request.Intersect, intersect);
        return ServerStream.ReadAllAsync<FollowTipCall, FollowTipResponse, FollowTipEvent, FollowTipEvent>((client, request), cancellationToken);
    }

    /// <include file="Docs.xml" path="docs/member[@name='DumpAllHistoryAsync']/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/stream/*[not(self::remarks)]"/>
    /// <include file="Docs.xml" path="docs/mask/*"/>
    /// <remarks><include file="Docs.xml" path="docs/member[@name='DumpAllHistoryAsync']/remarks/*"/> <include file="Docs.xml" path="docs/stream/remarks/*"/></remarks>
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

    private static void AddPoints(RepeatedField<BlockRef> target, ReadOnlySpan<BlockPoint> points)
    {
        target.Capacity = points.Length;
        for (int i = 0; i < points.Length; i++)
        {
            target.Add(new BlockRef { Slot = points[i].Slot, Hash = UnsafeByteOperations.UnsafeWrap(points[i].Hash) });
        }
    }
}
