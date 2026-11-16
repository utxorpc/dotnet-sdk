using Google.Protobuf;
using Grpc.Core;
using Alpha = Utxorpc.V1Alpha.Submit;
using Beta = Utxorpc.V1Beta.Submit;

namespace Utxorpc.Sdk.Test.Fakes;

internal sealed class FakeSubmitV1Alpha(FakeState state) : Alpha.SubmitService.SubmitServiceBase
{
    /// <summary>Returns up to the first 32 raw transaction bytes as its reference.</summary>
    public override Task<Alpha.SubmitTxResponse> SubmitTx(Alpha.SubmitTxRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Alpha.SubmitTxResponse
        {
            Ref = ByteString.CopyFrom(request.Tx.Raw.Span[..Math.Min(32, request.Tx.Raw.Length)])
        });
    }

    /// <summary>Writes three transaction stages and waits for the caller to close the stream.</summary>
    public override async Task WaitForTx(Alpha.WaitForTxRequest request, IServerStreamWriter<Alpha.WaitForTxResponse> responseStream, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        try
        {
            foreach (Alpha.Stage stage in new[] { Alpha.Stage.Acknowledged, Alpha.Stage.Mempool, Alpha.Stage.Confirmed })
            {
                await responseStream.WriteAsync(new() { Ref = request.Ref[0], Stage = stage });
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

internal sealed class FakeSubmitV1Beta(FakeState state) : Beta.SubmitService.SubmitServiceBase
{
    /// <summary>Returns up to the first 32 raw transaction bytes as its reference.</summary>
    public override Task<Beta.SubmitTxResponse> SubmitTx(Beta.SubmitTxRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Beta.SubmitTxResponse
        {
            Ref = ByteString.CopyFrom(request.Tx.Raw.Span[..Math.Min(32, request.Tx.Raw.Length)])
        });
    }

    /// <summary>Writes three transaction stages and waits for the caller to close the stream.</summary>
    public override async Task WaitForTx(Beta.WaitForTxRequest request, IServerStreamWriter<Beta.WaitForTxResponse> responseStream, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        try
        {
            foreach (Beta.Stage stage in new[] { Beta.Stage.Acknowledged, Beta.Stage.Mempool, Beta.Stage.Confirmed })
            {
                await responseStream.WriteAsync(new() { Ref = request.Ref[0], Stage = stage });
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
