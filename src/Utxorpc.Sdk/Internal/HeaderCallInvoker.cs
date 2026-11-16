using Grpc.Core;

namespace Utxorpc.Sdk.Internal;

internal sealed class HeaderCallInvoker(CallInvoker inner, Metadata headers) : CallInvoker
{
    /// <summary>Forwards a blocking unary call with the client headers.</summary>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        inner.BlockingUnaryCall(method, host, WithHeaders(options), request);

    /// <summary>Forwards an asynchronous unary call with the client headers.</summary>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        inner.AsyncUnaryCall(method, host, WithHeaders(options), request);

    /// <summary>Forwards a server stream with the client headers.</summary>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        inner.AsyncServerStreamingCall(method, host, WithHeaders(options), request);

    /// <summary>Forwards a client stream with the client headers.</summary>
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        inner.AsyncClientStreamingCall(method, host, WithHeaders(options));

    /// <summary>Forwards a duplex stream with the client headers.</summary>
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        inner.AsyncDuplexStreamingCall(method, host, WithHeaders(options));

    private CallOptions WithHeaders(CallOptions options)
    {
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
}
