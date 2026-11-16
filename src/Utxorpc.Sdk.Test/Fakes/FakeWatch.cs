using Google.Protobuf;
using Grpc.Core;
using Alpha = Utxorpc.V1Alpha.Watch;
using Beta = Utxorpc.V1Beta.Watch;

namespace Utxorpc.Sdk.Test.Fakes;

internal sealed class FakeWatchV1Alpha(FakeState state) : Alpha.WatchService.WatchServiceBase
{
    /// <summary>Writes two applied transactions and waits for the caller to close the stream.</summary>
    public override async Task WatchTx(Alpha.WatchTxRequest request, IServerStreamWriter<Alpha.WatchTxResponse> responseStream, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        try
        {
            for (int i = 1; i <= 2; i++)
            {
                await responseStream.WriteAsync(new() { Apply = new() { Block = new() { NativeBytes = ByteString.CopyFrom(new byte[] { (byte)i }) } } });
            }
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            state.StreamEnded.TrySetResult();
        }
    }
}

internal sealed class FakeWatchV1Beta(FakeState state) : Beta.WatchService.WatchServiceBase
{
    /// <summary>Writes two applied transactions and waits for the caller to close the stream.</summary>
    public override async Task WatchTx(Beta.WatchTxRequest request, IServerStreamWriter<Beta.WatchTxResponse> responseStream, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        try
        {
            for (int i = 1; i <= 2; i++)
            {
                await responseStream.WriteAsync(new() { Apply = new() { Block = new() { NativeBytes = ByteString.CopyFrom(new byte[] { (byte)i }) } } });
            }
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            state.StreamEnded.TrySetResult();
        }
    }
}
