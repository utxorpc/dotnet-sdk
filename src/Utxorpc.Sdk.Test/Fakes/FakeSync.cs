using Grpc.Core;
using Alpha = Utxorpc.V1Alpha;
using Beta = Utxorpc.V1Beta;

namespace Utxorpc.Sdk.Test.Fakes;

internal sealed class FakeSyncV1Alpha(FakeState state) : Alpha.Sync.SyncService.SyncServiceBase
{
    /// <summary>Answers with the fake tip after recording the call.</summary>
    public override Task<Alpha.Sync.ReadTipResponse> ReadTip(Alpha.Sync.ReadTipRequest request, ServerCallContext context)
    {
        state.Enter(context);
        return Task.FromResult(new Alpha.Sync.ReadTipResponse { Tip = new() { Slot = 42 } });
    }

    /// <summary>Answers with the configured parsed vector.</summary>
    public override Task<Alpha.Sync.FetchBlockResponse> FetchBlock(Alpha.Sync.FetchBlockRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Alpha.Sync.FetchBlockResponse
        {
            Block = { new Alpha.Sync.AnyChainBlock { Cardano = Alpha.Cardano.Block.Parser.ParseFrom(state.BlockBytes!) } }
        });
    }

    /// <summary>Writes the configured slots and waits for the caller to close the stream.</summary>
    public override async Task FollowTip(Alpha.Sync.FollowTipRequest request, IServerStreamWriter<Alpha.Sync.FollowTipResponse> responseStream, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        try
        {
            for (int i = 1; i <= state.StreamMessages; i++)
            {
                await responseStream.WriteAsync(new() { Reset = new() { Slot = (ulong)i } });
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

internal sealed class FakeSyncV1Beta(FakeState state) : Beta.Sync.SyncService.SyncServiceBase
{
    /// <summary>Answers with the fake tip after recording the call.</summary>
    public override Task<Beta.Sync.ReadTipResponse> ReadTip(Beta.Sync.ReadTipRequest request, ServerCallContext context)
    {
        state.Enter(context);
        return Task.FromResult(new Beta.Sync.ReadTipResponse { Tip = new() { Slot = 42 } });
    }

    /// <summary>Answers with the configured parsed vector.</summary>
    public override Task<Beta.Sync.FetchBlockResponse> FetchBlock(Beta.Sync.FetchBlockRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Beta.Sync.FetchBlockResponse
        {
            Block = { new Beta.Sync.AnyChainBlock { Cardano = Beta.Cardano.Block.Parser.ParseFrom(state.BlockBytes!) } }
        });
    }

    /// <summary>Writes the configured slots and waits for the caller to close the stream.</summary>
    public override async Task FollowTip(Beta.Sync.FollowTipRequest request, IServerStreamWriter<Beta.Sync.FollowTipResponse> responseStream, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        try
        {
            for (int i = 1; i <= state.StreamMessages; i++)
            {
                await responseStream.WriteAsync(new() { Reset = new() { Slot = (ulong)i } });
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
