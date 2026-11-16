using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Utxorpc.Sdk.Internal;

/// <summary>Starts a server stream and maps its messages; chosen by type, so the one reading loop stores no delegate.</summary>
internal interface IServerStream<TState, TMessage, TResult>
{
    /// <summary>Starts the call when enumeration starts.</summary>
    static abstract AsyncServerStreamingCall<TMessage> Start(TState state, CancellationToken cancellationToken);

    /// <summary>Maps one message.</summary>
    static abstract TResult Map(TMessage message);
}

/// <summary>Reads a call the caller started and yields each message unchanged.</summary>
internal readonly struct StartedCall<T> : IServerStream<AsyncServerStreamingCall<T>, T, T>
{
    /// <summary>Returns the caller's call.</summary>
    public static AsyncServerStreamingCall<T> Start(AsyncServerStreamingCall<T> state, CancellationToken cancellationToken) => state;

    /// <summary>Returns the message.</summary>
    public static T Map(T message) => message;
}

/// <summary>The one loop that reads server streams.</summary>
internal static class ServerStream
{
    /// <summary>Starts the call when enumeration starts, maps each response and disposes the call when enumeration ends.</summary>
    internal static async IAsyncEnumerable<TResult> ReadAllAsync<TState, T, TResult, TStream>(TState state, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TStream : IServerStream<TState, T, TResult>
    {
        using AsyncServerStreamingCall<T> call = TStream.Start(state, cancellationToken);
        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
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
