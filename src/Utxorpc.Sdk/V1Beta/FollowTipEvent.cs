using System.Runtime.CompilerServices;
using Grpc.Core;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Beta.Sync;
using FollowTipCall = (Utxorpc.V1Beta.Sync.SyncService.SyncServiceClient Client, Utxorpc.V1Beta.Sync.FollowTipRequest Request);

namespace Utxorpc.Sdk.V1Beta;

/// <summary>One message of the v1beta FollowTip stream: a block to apply, a block to undo, or a point to reset to.</summary>
/// <remarks>Read it with a switch expression over <see cref="BlockApplied"/>, <see cref="BlockUndone"/> and <see cref="Reset"/>: a missing case is compiler warning CS8509 (a switch statement is not checked), and reading the cases allocates nothing. <see cref="Value"/> boxes.</remarks>
[Union]
public readonly struct FollowTipEvent : IUnion, IServerStream<FollowTipCall, FollowTipResponse, FollowTipEvent>
{
    private readonly FollowTipResponse? response;

    private FollowTipEvent(FollowTipResponse response) => this.response = response;

    /// <summary>Creates an event that applies a block.</summary>
    public FollowTipEvent(BlockApplied value) => response = new() { Apply = value.Block ?? throw new ArgumentNullException(nameof(value)) };

    /// <summary>Creates an event that undoes a block.</summary>
    public FollowTipEvent(BlockUndone value) => response = new() { Undo = value.Block ?? throw new ArgumentNullException(nameof(value)) };

    /// <summary>Creates an event that resets to a point.</summary>
    public FollowTipEvent(Reset value) => response = new() { Reset = value.Point ?? throw new ArgumentNullException(nameof(value)) };

    /// <summary>Gets the Spec message this event reads; changing the message's action changes what the event reads.</summary>
    public FollowTipResponse Response => response ?? throw new InvalidOperationException("A default FollowTipEvent holds no message.");

    /// <summary>Gets the chain tip after this event, when the server sent it.</summary>
    public BlockRef? Tip => response?.Tip;

    /// <summary>Gets the case as an object; this boxes, so prefer a switch.</summary>
    public object? Value => response?.ActionCase switch
    {
        FollowTipResponse.ActionOneofCase.Apply => new BlockApplied(response.Apply),
        FollowTipResponse.ActionOneofCase.Undo => new BlockUndone(response.Undo),
        FollowTipResponse.ActionOneofCase.Reset => new Reset(response.Reset),
        _ => null,
    };

    /// <summary>Gets whether the event holds a case.</summary>
    public bool HasValue => response is { ActionCase: not FollowTipResponse.ActionOneofCase.None };

    /// <summary>Gets the applied block, if this event applies one.</summary>
    public bool TryGetValue(out BlockApplied value)
    {
        value = response is { ActionCase: FollowTipResponse.ActionOneofCase.Apply } ? new(response.Apply) : default;
        return value.Block is not null;
    }

    /// <summary>Gets the undone block, if this event undoes one.</summary>
    public bool TryGetValue(out BlockUndone value)
    {
        value = response is { ActionCase: FollowTipResponse.ActionOneofCase.Undo } ? new(response.Undo) : default;
        return value.Block is not null;
    }

    /// <summary>Gets the reset point, if this event resets.</summary>
    public bool TryGetValue(out Reset value)
    {
        value = response is { ActionCase: FollowTipResponse.ActionOneofCase.Reset } ? new(response.Reset) : default;
        return value.Point is not null;
    }

    static AsyncServerStreamingCall<FollowTipResponse> IServerStream<FollowTipCall, FollowTipResponse, FollowTipEvent>.Start(FollowTipCall call, CancellationToken cancellationToken) =>
        call.Client.FollowTip(call.Request, cancellationToken: cancellationToken);

    static FollowTipEvent IServerStream<FollowTipCall, FollowTipResponse, FollowTipEvent>.Map(FollowTipResponse message) =>
        message.ActionCase is FollowTipResponse.ActionOneofCase.None ? throw StreamErrors.UnknownAction("FollowTip") : new(message);
}

/// <summary>A block to apply.</summary>
/// <param name="Block">The block.</param>
public readonly record struct BlockApplied(AnyChainBlock Block);

/// <summary>A block to undo.</summary>
/// <param name="Block">The block.</param>
public readonly record struct BlockUndone(AnyChainBlock Block);

/// <summary>A point to reset to.</summary>
/// <param name="Point">The point.</param>
public readonly record struct Reset(BlockRef Point);
