using Google.Protobuf;
using Grpc.Core;
using Alpha = Utxorpc.V1Alpha.Query;
using Beta = Utxorpc.V1Beta.Query;

namespace Utxorpc.Sdk.Test.Fakes;

internal sealed class FakeQueryV1Alpha(FakeState state) : Alpha.QueryService.QueryServiceBase
{
    /// <summary>Returns a fixed parameter snapshot tip.</summary>
    public override Task<Alpha.ReadParamsResponse> ReadParams(Alpha.ReadParamsRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Alpha.ReadParamsResponse { LedgerTip = new() { Slot = 7 } });
    }

    /// <summary>Returns one output for every requested reference.</summary>
    public override Task<Alpha.ReadUtxosResponse> ReadUtxos(Alpha.ReadUtxosRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        Alpha.ReadUtxosResponse response = new();
        for (int i = 0; i < request.Keys.Count; i++)
        {
            response.Items.Add(new Alpha.AnyUtxoData { TxoRef = request.Keys[i] });
        }
        return Task.FromResult(response);
    }

    /// <summary>Returns three search pages, optionally holding the second until cancellation.</summary>
    public override async Task<Alpha.SearchUtxosResponse> SearchUtxos(Alpha.SearchUtxosRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        int page = request.StartToken switch { "p2" => 2, "p3" => 3, _ => 1 };
        if (page == 2 && state.HoldSecondPage)
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        Alpha.SearchUtxosResponse response = new()
        {
            Items = { new Alpha.AnyUtxoData { NativeBytes = ByteString.CopyFrom(new byte[] { (byte)page }) } }
        };
        response.NextToken = page < 3 ? $"p{page + 1}" : "";
        return response;
    }

    /// <summary>Echoes the requested hash as transaction bytes.</summary>
    public override Task<Alpha.ReadTxResponse> ReadTx(Alpha.ReadTxRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Alpha.ReadTxResponse { Tx = new() { NativeBytes = request.Hash } });
    }
}

internal sealed class FakeQueryV1Beta(FakeState state) : Beta.QueryService.QueryServiceBase
{
    /// <summary>Returns a fixed parameter snapshot tip.</summary>
    public override Task<Beta.ReadParamsResponse> ReadParams(Beta.ReadParamsRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Beta.ReadParamsResponse { LedgerTip = new() { Slot = 7 } });
    }

    /// <summary>Returns one output for every requested reference.</summary>
    public override Task<Beta.ReadUtxosResponse> ReadUtxos(Beta.ReadUtxosRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        Beta.ReadUtxosResponse response = new();
        for (int i = 0; i < request.Keys.Count; i++)
        {
            response.Items.Add(new Beta.AnyUtxoData { TxoRef = request.Keys[i] });
        }
        return Task.FromResult(response);
    }

    /// <summary>Returns three search pages, optionally holding the second until cancellation.</summary>
    public override async Task<Beta.SearchUtxosResponse> SearchUtxos(Beta.SearchUtxosRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        int page = request.StartToken switch { "p2" => 2, "p3" => 3, _ => 1 };
        if (page == 2 && state.HoldSecondPage)
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        Beta.SearchUtxosResponse response = new()
        {
            Items = { new Beta.AnyUtxoData { NativeBytes = ByteString.CopyFrom(new byte[] { (byte)page }) } }
        };
        if (page < 3) response.NextToken = $"p{page + 1}";
        return response;
    }

    /// <summary>Echoes the requested hash as transaction bytes.</summary>
    public override Task<Beta.ReadTxResponse> ReadTx(Beta.ReadTxRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Beta.ReadTxResponse { Tx = new() { NativeBytes = request.Hash } });
    }

    /// <summary>Returns a ledger tip for the typed state query.</summary>
    public override Task<Beta.ReadStateResponse> ReadState(Beta.ReadStateRequest request, ServerCallContext context)
    {
        state.Enter(context);
        state.Requests.Enqueue(request);
        return Task.FromResult(new Beta.ReadStateResponse { LedgerTip = new() { Slot = 9 } });
    }
}
