using Google.Protobuf;
using Grpc.Core;

namespace Utxorpc.Sdk;

/// <summary>The error a short method throws: the gRPC error with what it means for the caller.</summary>
public sealed class UtxorpcException : RpcException
{
    internal UtxorpcException(UtxorpcErrorKind kind, RpcException inner)
        : base(new Status(inner.StatusCode, inner.Status.Detail, inner), inner.Trailers,
            inner.Status.Detail.Length == 0 ? $"{kind} ({inner.StatusCode})" : $"{kind}: {inner.Status.Detail}")
    {
        Kind = kind;
    }

    /// <summary>Gets what the failure means for the caller.</summary>
    public UtxorpcErrorKind Kind { get; }

    /// <summary>Gets the server's own words, or the transport's.</summary>
    public string Detail => Status.Detail;

    /// <summary>Says whether trying the same call again may work.</summary>
    /// <param name="exception">The error of a short method or generated call.</param>
    /// <returns>Whether the failure may be temporary.</returns>
    /// <exception cref="ArgumentNullException">The exception is null.</exception>
    public static bool IsTransient(RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        RpcException original = Root(exception);
        for (Exception? cause = original.Status.DebugException; cause is not null; cause = cause.InnerException)
        {
            if (cause is InvalidProtocolBufferException) return false;
        }
        return original.StatusCode is StatusCode.Unavailable ||
            (original.StatusCode is StatusCode.Internal && original.Status.DebugException is not null);
    }

    private static RpcException Root(RpcException exception) =>
        exception is UtxorpcException { InnerException: RpcException inner } ? inner : exception;
}
