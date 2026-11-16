using Google.Protobuf;
using Utxorpc.V1Alpha.Sync;

namespace Utxorpc.Sdk;

/// <summary>Provides plain .NET inputs for the v1alpha synchronization client.</summary>
public static class V1AlphaSyncExtensions
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
}
