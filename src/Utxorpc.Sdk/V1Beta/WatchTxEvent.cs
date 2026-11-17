using System.Runtime.CompilerServices;
using Grpc.Core;
using Utxorpc.Sdk.Internal;
using Utxorpc.V1Beta.Watch;
using WatchTxCall = (Utxorpc.V1Beta.Watch.WatchService.WatchServiceClient Client, Utxorpc.V1Beta.Watch.WatchTxRequest Request);

namespace Utxorpc.Sdk.V1Beta;

/// <summary>One message of the v1beta WatchTx stream: a transaction to apply, a transaction to undo, or a block without a match.</summary>
/// <remarks>Read it with a switch expression over <see cref="TxApplied"/>, <see cref="TxUndone"/> and <see cref="Idle"/>: a missing case is compiler warning CS8509 (a switch statement is not checked), and reading the cases allocates nothing. <see cref="Value"/> boxes. End the switch with <c>null =&gt; throw new UnreachableException()</c>: a default event holds no case.</remarks>
[Union]
public readonly struct WatchTxEvent : IUnion, IServerStream<WatchTxCall, WatchTxResponse, WatchTxEvent>
{
    private readonly WatchTxResponse? response;

    private WatchTxEvent(WatchTxResponse response) => this.response = response;

    /// <summary>Creates an event that applies a transaction.</summary>
    /// <include file="../Docs.xml" path="docs/member[@name='eventCase']/*"/>
    public WatchTxEvent(TxApplied value) => response = new() { Apply = value.Tx ?? throw new ArgumentNullException(nameof(value)) };

    /// <summary>Creates an event that undoes a transaction.</summary>
    /// <include file="../Docs.xml" path="docs/member[@name='eventCase']/*"/>
    public WatchTxEvent(TxUndone value) => response = new() { Undo = value.Tx ?? throw new ArgumentNullException(nameof(value)) };

    /// <summary>Creates an event for a block without a match.</summary>
    /// <include file="../Docs.xml" path="docs/member[@name='eventCase']/*"/>
    public WatchTxEvent(Idle value) => response = new() { Idle = value.Point ?? throw new ArgumentNullException(nameof(value)) };

    /// <summary>Gets the Spec message this event reads; changing the message's action changes what the event reads.</summary>
    public WatchTxResponse Response => response ?? throw new InvalidOperationException("A default WatchTxEvent holds no message.");

    /// <summary>Gets the case as an object; this boxes, so prefer a switch.</summary>
    public object? Value => response?.ActionCase switch
    {
        WatchTxResponse.ActionOneofCase.Apply => new TxApplied(response.Apply),
        WatchTxResponse.ActionOneofCase.Undo => new TxUndone(response.Undo),
        WatchTxResponse.ActionOneofCase.Idle => new Idle(response.Idle),
        _ => null,
    };

    /// <summary>Gets whether the event holds a case.</summary>
    public bool HasValue => response is { ActionCase: not WatchTxResponse.ActionOneofCase.None };

    /// <summary>Gets the applied transaction, if this event applies one.</summary>
    public bool TryGetValue(out TxApplied value)
    {
        value = response is { ActionCase: WatchTxResponse.ActionOneofCase.Apply } ? new(response.Apply) : default;
        return value.Tx is not null;
    }

    /// <summary>Gets the undone transaction, if this event undoes one.</summary>
    public bool TryGetValue(out TxUndone value)
    {
        value = response is { ActionCase: WatchTxResponse.ActionOneofCase.Undo } ? new(response.Undo) : default;
        return value.Tx is not null;
    }

    /// <summary>Gets the idle block, if this event is one.</summary>
    public bool TryGetValue(out Idle value)
    {
        value = response is { ActionCase: WatchTxResponse.ActionOneofCase.Idle } ? new(response.Idle) : default;
        return value.Point is not null;
    }

    static AsyncServerStreamingCall<WatchTxResponse> IServerStream<WatchTxCall, WatchTxResponse, WatchTxEvent>.Start(WatchTxCall call, CancellationToken cancellationToken) =>
        call.Client.WatchTx(call.Request, cancellationToken: cancellationToken);

    static WatchTxEvent IServerStream<WatchTxCall, WatchTxResponse, WatchTxEvent>.Map(WatchTxResponse message) =>
        message.ActionCase is WatchTxResponse.ActionOneofCase.None ? throw StreamErrors.UnknownAction("WatchTx") : new(message);
}

/// <summary>A transaction to apply.</summary>
/// <param name="Tx">The transaction.</param>
public readonly record struct TxApplied(AnyChainTx Tx);

/// <summary>A transaction to undo.</summary>
/// <param name="Tx">The transaction.</param>
public readonly record struct TxUndone(AnyChainTx Tx);

/// <summary>A block in which nothing matched.</summary>
/// <param name="Point">The point.</param>
public readonly record struct Idle(BlockRef Point);
