using Google.Protobuf;
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
            for (int i = 1; i <= (state.MixedActions ? 4 : state.StreamMessages); i++)
            {
                await responseStream.WriteAsync(state.MixedActions ? i switch
                {
                    1 => new() { Apply = new() { NativeBytes = ByteString.CopyFrom(new byte[] { 1 }) }, Tip = new() { Slot = 101 } },
                    2 => new() { Undo = new() { NativeBytes = ByteString.CopyFrom(new byte[] { 2 }) }, Tip = new() { Slot = 102 } },
                    3 => new() { Reset = new() { Slot = 3 }, Tip = new() { Slot = 103 } },
                    _ => new() { Tip = new() { Slot = 104 } }
                } : new() { Reset = new() { Slot = (ulong)i } });
                if (state.AbortAfterFirstMessage)
                {
                    await Task.Delay(50, context.CancellationToken);
                    context.GetHttpContext().Abort();
                    return;
                }
                if (state.FailAfterFirstMessage) throw new RpcException(new Status(StatusCode.Unavailable, "fake"));
            }
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            state.StreamEnded.TrySetResult();
        }
    }

    /// <summary>Returns five blocks across three pages with block-reference tokens.</summary>
    public override Task<Alpha.Sync.DumpHistoryResponse> DumpHistory(Alpha.Sync.DumpHistoryRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        ulong first = request.StartToken?.Slot ?? 1;
        Alpha.Sync.DumpHistoryResponse response = new();
        for (ulong slot = first; slot <= Math.Min(first + 1, 5UL); slot++)
        {
            response.Block.Add(new Alpha.Sync.AnyChainBlock { NativeBytes = ByteString.CopyFrom(new byte[] { (byte)slot }) });
        }
        if (first < 5) response.NextToken = new() { Slot = first + 2 };
        return Task.FromResult(response);
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
            for (int i = 1; i <= (state.MixedActions ? 4 : state.StreamMessages); i++)
            {
                await responseStream.WriteAsync(state.MixedActions ? i switch
                {
                    1 => new() { Apply = new() { NativeBytes = ByteString.CopyFrom(new byte[] { 1 }) }, Tip = new() { Slot = 101 } },
                    2 => new() { Undo = new() { NativeBytes = ByteString.CopyFrom(new byte[] { 2 }) }, Tip = new() { Slot = 102 } },
                    3 => new() { Reset = new() { Slot = 3 }, Tip = new() { Slot = 103 } },
                    _ => new() { Tip = new() { Slot = 104 } }
                } : new() { Reset = new() { Slot = (ulong)i } });
                if (state.AbortAfterFirstMessage)
                {
                    await Task.Delay(50, context.CancellationToken);
                    context.GetHttpContext().Abort();
                    return;
                }
                if (state.FailAfterFirstMessage) throw new RpcException(new Status(StatusCode.Unavailable, "fake"));
            }
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            state.StreamEnded.TrySetResult();
        }
    }

    /// <summary>Returns five blocks across three pages with block-reference tokens.</summary>
    public override Task<Beta.Sync.DumpHistoryResponse> DumpHistory(Beta.Sync.DumpHistoryRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        ulong first = request.StartToken?.Slot ?? 1;
        Beta.Sync.DumpHistoryResponse response = new();
        for (ulong slot = first; slot <= Math.Min(first + 1, 5UL); slot++)
        {
            response.Block.Add(new Beta.Sync.AnyChainBlock { NativeBytes = ByteString.CopyFrom(new byte[] { (byte)slot }) });
        }
        if (first < 5) response.NextToken = new() { Slot = first + 2 };
        return Task.FromResult(response);
    }
}
