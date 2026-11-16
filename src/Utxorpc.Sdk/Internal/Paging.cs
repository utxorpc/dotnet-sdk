using System.Runtime.CompilerServices;
using Google.Protobuf.Collections;

namespace Utxorpc.Sdk.Internal;

internal static class Paging
{
    internal static async IAsyncEnumerable<TItem> ReadAllPagesAsync<TClient, TState, TResponse, TItem>(
        TClient client, TState state,
        Func<TClient, TState, string?, CancellationToken, Task<TResponse>> fetchPage,
        Func<TResponse, RepeatedField<TItem>> items,
        Func<TResponse, string?> nextToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? token = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            TResponse response = await fetchPage(client, state, token, cancellationToken).ConfigureAwait(false);
            RepeatedField<TItem> pageItems = items(response);
            for (int i = 0; i < pageItems.Count; i++)
            {
                yield return pageItems[i];
            }
            token = nextToken(response);
        }
        while (!string.IsNullOrEmpty(token));
    }
}
