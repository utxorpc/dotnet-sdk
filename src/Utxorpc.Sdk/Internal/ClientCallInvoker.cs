using Grpc.Core;

namespace Utxorpc.Sdk.Internal;

/// <summary>Adds the client's headers and default timeout to calls, rejects a null request, and reports an expired deadline as DeadlineExceeded.</summary>
internal sealed class ClientCallInvoker(CallInvoker inner, Metadata headers, TimeSpan defaultTimeout) : CallInvoker
{
    /// <summary>Forwards a blocking unary call.</summary>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        options = WithTimeout(WithHeaders(options));
        try
        {
            return inner.BlockingUnaryCall(method, host, options, request);
        }
        catch (OperationCanceledException exception) when (options.Deadline is DateTime deadline && DeadlinePassed(deadline, options.CancellationToken))
        {
            throw DeadlineExceeded(exception);
        }
    }

    /// <summary>Forwards an asynchronous unary call; one with a deadline that is still running gets tasks that report its expiry.</summary>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        options = WithTimeout(WithHeaders(options));
        AsyncUnaryCall<TResponse> call = inner.AsyncUnaryCall(method, host, options, request);
        if (options.Deadline is not DateTime deadline || (call.ResponseAsync.IsCompletedSuccessfully && call.ResponseHeadersAsync.IsCompletedSuccessfully))
        {
            return call;
        }

        UnaryDeadlineCall<TResponse> state = new(call, deadline, options.CancellationToken);
        return new AsyncUnaryCall<TResponse>(
            state.Response,
            static state => ((DeadlineCall<TResponse>)state).Headers(((DeadlineCall<TResponse>)state).Unary.ResponseHeadersAsync),
            static state => ((DeadlineCall<TResponse>)state).Unary.GetStatus(),
            static state => ((DeadlineCall<TResponse>)state).Unary.GetTrailers(),
            static state => ((UnaryDeadlineCall<TResponse>)state).Dispose(),
            state);
    }

    /// <summary>Forwards a server stream, never with the default timeout; one with a deadline gets a reader and a headers task that report its expiry.</summary>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AsyncServerStreamingCall<TResponse> call = inner.AsyncServerStreamingCall(method, host, WithHeaders(options), request);
        if (options.Deadline is not DateTime deadline)
        {
            return call;
        }

        DeadlineCall<TResponse> state = new(call, deadline, options.CancellationToken);
        return new AsyncServerStreamingCall<TResponse>(
            state,
            static state => ((DeadlineCall<TResponse>)state).Headers(((DeadlineCall<TResponse>)state).Stream.ResponseHeadersAsync),
            static state => ((DeadlineCall<TResponse>)state).Stream.GetStatus(),
            static state => ((DeadlineCall<TResponse>)state).Stream.GetTrailers(),
            static state => ((DeadlineCall<TResponse>)state).Stream.Dispose(),
            state);
    }

    /// <summary>Forwards a client stream with the client headers.</summary>
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        inner.AsyncClientStreamingCall(method, host, WithHeaders(options));

    /// <summary>Forwards a duplex stream with the client headers.</summary>
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        inner.AsyncDuplexStreamingCall(method, host, WithHeaders(options));

    // The deadline wins only when the caller's token is not cancelled. A server that enforces the deadline may
    // report it up to 1 ms early: the timeout is sent in whole milliseconds, rounded down.
    private static bool DeadlinePassed(DateTime deadline, CancellationToken callerToken) =>
        !callerToken.IsCancellationRequested && DateTime.UtcNow.AddMilliseconds(1) >= deadline.ToUniversalTime();

    private static RpcException DeadlineExceeded(OperationCanceledException exception) =>
        new(new Status(StatusCode.DeadlineExceeded, string.Empty, exception));

    // A unary call without its own deadline gets the client's default timeout.
    private CallOptions WithTimeout(CallOptions options) =>
        defaultTimeout == Timeout.InfiniteTimeSpan || options.Deadline is not null ? options : options.WithDeadline(DateTime.UtcNow.Add(defaultTimeout));

    private CallOptions WithHeaders(CallOptions options)
    {
        if (headers.Count == 0)
        {
            return options;
        }

        if (options.Headers is null || options.Headers.Count == 0)
        {
            return options.WithHeaders(headers);
        }

        Metadata merged = new();
        for (int i = 0; i < headers.Count; i++)
        {
            merged.Add(headers[i]);
        }
        for (int i = 0; i < options.Headers.Count; i++)
        {
            merged.Add(options.Headers[i]);
        }
        return options.WithHeaders(merged);
    }

    // One per running call with a deadline: the state of the call the caller gets, and a stream's reader.
    private class DeadlineCall<T>(IDisposable call, DateTime deadline, CancellationToken callerToken) : IAsyncStreamReader<T>
    {
        internal AsyncUnaryCall<T> Unary => (AsyncUnaryCall<T>)call;

        internal AsyncServerStreamingCall<T> Stream => (AsyncServerStreamingCall<T>)call;

        /// <summary>Gets the current message.</summary>
        public T Current => Stream.ResponseStream.Current;

        /// <summary>Reads the next message; only a read that has to wait is awaited here.</summary>
        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            Task<bool> next = Stream.ResponseStream.MoveNext(cancellationToken);
            return next.IsCompletedSuccessfully ? next : OrDeadlineExceeded(next, cancellationToken);
        }

        // Headers that have arrived are returned as they are; the task is wrapped only when someone reads it.
        internal Task<Metadata> Headers(Task<Metadata> responseHeaders) =>
            responseHeaders.IsCompletedSuccessfully ? responseHeaders : OrDeadlineExceeded(responseHeaders, default);

        internal async Task<TResult> OrDeadlineExceeded<TResult>(Task<TResult> task, CancellationToken readToken)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Failed headers can leave the unary response unread, even when the call is never disposed.
                if (this is UnaryDeadlineCall<T> { Response: not null } unary) unary.ObserveResponse();
                if (exception is OperationCanceledException cancelled)
                {
                    if (readToken.IsCancellationRequested) callerToken = readToken;
                    else if (DeadlinePassed(deadline, callerToken)) throw DeadlineExceeded(cancelled);
                }
                throw;
            }
        }
    }

    // Keep the response only for unary calls, without enlarging each stream's state.
    private sealed class UnaryDeadlineCall<T> : DeadlineCall<T>
    {
        internal UnaryDeadlineCall(AsyncUnaryCall<T> call, DateTime deadline, CancellationToken callerToken) : base(call, deadline, callerToken) =>
            Response = OrDeadlineExceeded(call.ResponseAsync, default);

        internal Task<T> Response { get; }

        internal void Dispose()
        {
            Unary.Dispose();
            ObserveResponse();
        }

        internal void ObserveResponse()
        {
            if (Response.IsFaulted) _ = Response.Exception;
            else if (!Response.IsCompleted)
                _ = Response.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
