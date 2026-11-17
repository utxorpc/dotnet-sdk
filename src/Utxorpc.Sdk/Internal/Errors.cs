using Grpc.Core;

namespace Utxorpc.Sdk.Internal;

/// <summary>Turns gRPC errors into the SDK's typed error, in one place.</summary>
internal static class Errors
{
    /// <summary>Returns a completed response as it is; a response still on its way is awaited so its failure can be typed.</summary>
    internal static Task<T> Typed<T>(Task<T> response, bool transaction = false) =>
        response.IsCompletedSuccessfully ? response : Await(response, transaction);

    /// <summary>Builds the typed error for a gRPC error.</summary>
    internal static UtxorpcException From(RpcException exception, bool transaction = false) => new(
        UtxorpcException.IsTransient(exception) ? UtxorpcErrorKind.Unavailable : exception.StatusCode switch
        {
            StatusCode.NotFound => UtxorpcErrorKind.NotFound,
            StatusCode.Unimplemented => UtxorpcErrorKind.Unsupported,
            StatusCode.DeadlineExceeded => UtxorpcErrorKind.Timeout,
            StatusCode.InvalidArgument or StatusCode.Unknown when transaction => UtxorpcErrorKind.InvalidTx,
            StatusCode.InvalidArgument => UtxorpcErrorKind.InvalidRequest,
            _ => UtxorpcErrorKind.Other,
        }, exception);

    private static async Task<T> Await<T>(Task<T> response, bool transaction)
    {
        try
        {
            return await response.ConfigureAwait(false);
        }
        catch (RpcException exception) when (exception is not UtxorpcException)
        {
            throw From(exception, transaction);
        }
    }
}
