using System.Runtime.CompilerServices;
using Google.Protobuf.Collections;
using Grpc.Core;

namespace Utxorpc.Sdk.Internal;

internal static class Paging
{
    internal static async IAsyncEnumerable<TItem> ReadAllPagesAsync<TClient, TState, TToken, TResponse, TItem>(
        TClient client, TState state,
        Func<TClient, TState, TToken?, CancellationToken, Task<TResponse>> fetchPage,
        Func<TResponse, RepeatedField<TItem>> items,
        Func<TResponse, TToken?> nextToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TToken : class
    {
        TToken? token = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            TResponse response;
            try
            {
                response = await fetchPage(client, state, token, cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException exception)
            {
                throw Errors.From(exception);
            }
            RepeatedField<TItem> pageItems = items(response);
            for (int i = 0; i < pageItems.Count; i++)
            {
                yield return pageItems[i];
            }
            token = nextToken(response);
        }
        while (token is not null);
    }
}
