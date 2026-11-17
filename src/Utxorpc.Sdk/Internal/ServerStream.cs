using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Utxorpc.Sdk.Internal;

/// <summary>Starts a server stream and maps its messages; chosen by type, so the one reading loop stores no delegate.</summary>
internal interface IServerStream<TState, TMessage, TResult>
{
    /// <summary>Gets whether failures are mapped to the SDK's typed error.</summary>
    static virtual bool TypedErrors => true;

    /// <summary>Starts the call when enumeration starts.</summary>
    static abstract AsyncServerStreamingCall<TMessage> Start(TState state, CancellationToken cancellationToken);

    /// <summary>Maps one message.</summary>
    static abstract TResult Map(TMessage message);
}

/// <summary>Reads chain positions, copies intersections and builds rollback events for a resumable stream.</summary>
internal interface IResumableStream<TState, TMessage>
{
    static abstract bool Read(TMessage message, out StreamAction action, out ChainBlock block);
    static abstract TState From(TState state, ReadOnlySpan<ChainBlock> intersect);
    static abstract TMessage Rollback(TMessage? applied, ChainBlock to);
    static abstract bool PerTransaction { get; }
    static abstract bool SameTransaction(TMessage applied, TMessage undo);
}

/// <summary>Reads a call the caller started and yields each message unchanged.</summary>
internal readonly struct StartedCall<T> : IServerStream<AsyncServerStreamingCall<T>, T, T>
{
    /// <summary>Leaves failures of a generated call unchanged.</summary>
    public static bool TypedErrors => false;

    /// <summary>Returns the caller's call.</summary>
    public static AsyncServerStreamingCall<T> Start(AsyncServerStreamingCall<T> state, CancellationToken cancellationToken) => state;

    /// <summary>Returns the message.</summary>
    public static T Map(T message) => message;
}

/// <summary>The one loop that reads server streams.</summary>
internal static class ServerStream
{
    internal static async IAsyncEnumerable<TResult> ReadAllResumingAsync<TState, T, TResult, TStream>(TState state, ResumeSettings settings, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TStream : IServerStream<TState, T, TResult>, IResumableStream<TState, T>
        where T : class
    {
        Window<T> window = new();
        ChainBlock[] points = new ChainBlock[10];
        TState request = state;
        RpcException? failure = null;
        bool resumable = true, yielded = false, placing = false;
        long attempt = 0;
        while (true)
        {
            Window<T>.Entry? replay = window.Newest is { Leaving: false } newest ? newest : null;
            int skip = placing && replay is not null ? (TStream.PerTransaction ? Math.Max(replay.Applied.Count, 1) : 1) : 0;
            AsyncServerStreamingCall<T>? call = null;
            RpcException? nextFailure = null;
            try { call = TStream.Start(request, cancellationToken); }
            catch (RpcException exception) { nextFailure = exception; }
            using (call)
            {
                while (call is not null)
                {
                    bool next;
                    try { next = await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false); }
                    catch (RpcException exception) { nextFailure = exception; break; }
                    if (!next) yield break;
                    T message = call.ResponseStream.Current;
                    bool readable = TStream.Read(message, out StreamAction action, out ChainBlock block);
                    if (placing)
                    {
                        if (!readable || action == StreamAction.Undo) throw Errors.From(failure!);
                        if (!TStream.PerTransaction && action == StreamAction.Reset)
                        {
                            Window<T>.Entry? chosen = window.Find(block);
                            if (chosen is null) throw Errors.From(failure!);
                            placing = false;
                            if (chosen == window.Newest) continue;
                            window.CutAfter(chosen);
                            skip = 0;
                        }
                        else if ((action is StreamAction.Apply or StreamAction.Idle) && skip > 0 && replay is not null && block.Is(replay.Block))
                        {
                            placing = false;
                            skip--;
                            continue;
                        }
                        else
                        {
                            Window<T>.Entry? chosen = window.FindBelow(block.Height);
                            if (chosen is null) throw Errors.From(failure!);
                            if (TStream.PerTransaction)
                            {
                                foreach (Window<T>.Entry entry in window.EntriesAfter(chosen))
                                    for (int i = entry.Applied.Count - 1; i >= 0; i--)
                                        yield return TStream.Map(TStream.Rollback(entry.Applied[i], chosen.Block));
                            }
                            else if (chosen != window.Newest)
                                yield return TStream.Map(TStream.Rollback(null, chosen.Block));
                            window.CutAfter(chosen);
                            placing = false;
                            skip = 0;
                        }
                    }
                    else if (skip > 0)
                    {
                        if (readable && (action is StreamAction.Apply or StreamAction.Idle) && replay is not null && block.Is(replay.Block))
                        {
                            skip--;
                            continue;
                        }
                        skip = 0;
                    }
                    if (!readable)
                    {
                        resumable = false;
                        window.Clear();
                    }
                    else if (resumable) Record<TState, T, TStream>(window, message, action, block);
                    yielded = true;
                    attempt = 0;
                    yield return TStream.Map(message);
                }
            }
            failure = nextFailure!;
            if (!resumable || !UtxorpcException.IsTransient(failure) || cancellationToken.IsCancellationRequested)
                throw Errors.From(failure);
            if (yielded)
            {
                int count = window.Intersect(points, leaveOutIncompleteNewest: TStream.PerTransaction);
                if (count == 0) throw Errors.From(failure);
                request = TStream.From(state, points.AsSpan(0, count));
            }
            attempt++;
            if (settings.Attempts is int limit && attempt > limit) throw Errors.From(failure);
            await Task.Delay(StreamResume.Backoff((int)Math.Min(attempt, int.MaxValue)), settings.Time, cancellationToken).ConfigureAwait(false);
            placing = yielded;
        }
    }

    private static void Record<TState, T, TStream>(Window<T> window, T message, StreamAction action, ChainBlock block)
        where TStream : IResumableStream<TState, T>
    {
        Window<T>.Entry? entry;
        if (!TStream.PerTransaction)
        {
            switch (action)
            {
                case StreamAction.Apply: window.Add(block); break;
                case StreamAction.Undo:
                    entry = window.Find(block);
                    if (entry is not null) { window.CutAfter(entry); window.Remove(entry); }
                    break;
                case StreamAction.Reset:
                    entry = window.Find(block);
                    if (entry is not null) window.CutAfter(entry);
                    else { window.Clear(); window.Add(block); }
                    break;
            }
        }
        else
        {
            switch (action)
            {
                case StreamAction.Apply:
                    entry = window.Newest;
                    if (entry is null || entry.Leaving || !entry.Block.Is(block)) entry = window.Add(block);
                    entry.Applied.Add(message);
                    break;
                case StreamAction.Idle:
                    if (window.Newest is not { } newest || !newest.Block.Is(block)) window.Add(block);
                    break;
                case StreamAction.Undo:
                    entry = window.Find(block);
                    if (entry is null) break;
                    entry.Leaving = true;
                    for (int i = entry.Applied.Count - 1; i >= 0; i--)
                    {
                        if (!TStream.SameTransaction(entry.Applied[i], message)) continue;
                        entry.Applied.RemoveAt(i);
                        break;
                    }
                    if (entry.Applied.Count == 0) window.Remove(entry);
                    break;
            }
        }
    }

    /// <summary>Starts the call when enumeration starts, maps each response and disposes the call when enumeration ends.</summary>
    internal static async IAsyncEnumerable<TResult> ReadAllAsync<TState, T, TResult, TStream>(TState state, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TStream : IServerStream<TState, T, TResult>
    {
        using AsyncServerStreamingCall<T> call = TStream.Start(state, cancellationToken);
        while (true)
        {
            bool next;
            try
            {
                next = await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException exception) when (TStream.TypedErrors)
            {
                throw Errors.From(exception);
            }
            if (!next) yield break;
            yield return TStream.Map(call.ResponseStream.Current);
        }
    }
}

/// <summary>Builds the one error for a stream message whose action this SDK does not know.</summary>
internal static class StreamErrors
{
    /// <summary>Names the call and how to read such messages.</summary>
    internal static InvalidOperationException UnknownAction(string call) =>
        new($"The server sent a {call} message without an action this SDK version knows; the generated {call} call returns the Spec message as is.");
}
