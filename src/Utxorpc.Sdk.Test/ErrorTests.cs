using Google.Protobuf;
using Grpc.Core;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks typed short-method failures against server answers and transport failures.</summary>
[Trait("Category", "Unit")]
public sealed class ErrorTests : ClientTestBase
{
    /// <summary>Preserves recorded statuses and details while classifying what failed.</summary>
    // Eleven answers recorded from Dolos v1.6.3 and Dingo v0.94.1 on preprod, 17 Nov 2026.
    // SearchAllUtxos uses Dolos v1.6.3's v1beta query handler, lines 911–913.
    [Theory]
    [InlineData("SubmitTx", StatusCode.InvalidArgument, "could not process tx: chain error: decoding error", UtxorpcErrorKind.InvalidTx)]
    [InlineData("SubmitTx", StatusCode.Unknown, "failed decoding tx: unknown transaction type", UtxorpcErrorKind.InvalidTx)]
    [InlineData("EvalTx", StatusCode.Unknown, "could not parse transaction to determine type: unknown transaction type", UtxorpcErrorKind.InvalidTx)]
    [InlineData("ReadTx", StatusCode.NotFound, "tx hash not found", UtxorpcErrorKind.NotFound)]
    [InlineData("ReadTx", StatusCode.NotFound, "transaction not found", UtxorpcErrorKind.NotFound)]
    [InlineData("FetchBlock", StatusCode.NotFound, "Failed to find block", UtxorpcErrorKind.NotFound)]
    [InlineData("FetchBlock", StatusCode.Unknown, "block not found", UtxorpcErrorKind.Other)]
    [InlineData("ReadUtxos", StatusCode.Unknown, "utxo not found", UtxorpcErrorKind.Other)]
    [InlineData("ReadMempool", StatusCode.Unimplemented, "read_mempool is not yet available", UtxorpcErrorKind.Unsupported)]
    [InlineData("ReadTx", StatusCode.InvalidArgument, "transaction hash must be 32 bytes", UtxorpcErrorKind.InvalidRequest)]
    [InlineData("SearchAllUtxos", StatusCode.InvalidArgument, "only 'match' predicate is supported by Dolos", UtxorpcErrorKind.InvalidRequest)]
    [InlineData("FollowTip", StatusCode.Unknown, "nil point returned", UtxorpcErrorKind.Other)]
    public async Task RecordedServerAnswersMapToKinds(string call, StatusCode status, string detail, UtxorpcErrorKind kind)
    {
        server.State.FailWith = status;
        server.State.FailDetail = detail;
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1 });
        ReadOnlyMemory<byte> hash = new byte[32];
        UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(async () =>
        {
            switch (call)
            {
                case "SubmitTx": await client.V1Beta.Submit.SubmitTxAsync(hash, cancellation.Token); break;
                case "EvalTx": await client.V1Beta.Submit.EvalTxAsync(hash, cancellation.Token); break;
                case "ReadTx": await client.V1Beta.Query.ReadTxAsync(hash, cancellationToken: cancellation.Token); break;
                case "FetchBlock": await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(1, hash), cancellationToken: cancellation.Token); break;
                case "ReadUtxos": await client.V1Beta.Query.ReadUtxosAsync([new(hash, 0)], cancellationToken: cancellation.Token); break;
                case "ReadMempool": await client.V1Beta.Submit.ReadMempoolAsync(cancellation.Token); break;
                case "SearchAllUtxos": await Read(client.V1Beta.Query.SearchAllUtxosAsync(new(), cancellationToken: cancellation.Token)); break;
                case "FollowTip": await Read(client.V1Beta.Sync.FollowTipAsync(cancellationToken: cancellation.Token)); break;
                default: throw new ArgumentOutOfRangeException(nameof(call));
            }
        });
        Assert.Equal(kind, error.Kind);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(detail, error.Detail);
        Assert.Equal($"{kind}: {detail}", error.Message);
        RpcException inner = Assert.IsType<RpcException>(error.InnerException);
        Assert.Equal(status, inner.StatusCode);
        Assert.Equal(detail, inner.Status.Detail);
        Assert.Same(inner, error.Status.DebugException);
        Assert.Same(inner.Trailers, error.Trailers);
    }

    /// <summary>Leaves generated unary and streaming errors as the plain gRPC type.</summary>
    [Fact]
    public async Task RawCallsStayPlain()
    {
        server.State.FailWith = StatusCode.NotFound;
        using UtxorpcClient client = new(server.Url);
        await Assert.ThrowsAsync<RpcException>(async () => await client.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: cancellation.Token));
        await Assert.ThrowsAsync<RpcException>(() => Read(client.V1Beta.Sync.FollowTip(new(), cancellationToken: cancellation.Token).ReadAllAsync(cancellation.Token)));
    }

    /// <summary>Does not map caller cancellation to a typed error on an SDK-created channel.</summary>
    [Fact]
    public async Task CancellationStaysOperationCanceled()
    {
        using UtxorpcClient client = new(server.Url);
        using CancellationTokenSource stop = new();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.V1Beta.Sync.ReadTipAsync(stop.Token));
        using CancellationTokenSource streaming = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        await using IAsyncEnumerator<V1Beta.FollowTipEvent> events = client.V1Beta.Sync.FollowTipAsync(cancellationToken: streaming.Token).GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());
        streaming.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { while (await events.MoveNextAsync()) { } });
    }

    /// <summary>Preserves server-sent deadline cancellation when the short call has no client deadline.</summary>
    [Fact]
    public async Task ServerDeadlineWithoutClientDeadlineStaysOperationCanceled()
    {
        server.State.FailWith = StatusCode.DeadlineExceeded;
        using UtxorpcClient client = new(server.Url);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.V1Beta.Sync.ReadTipAsync(cancellation.Token));
        Assert.False(cancellation.IsCancellationRequested);
        Assert.Equal("/utxorpc.v1beta.sync.SyncService/ReadTip", Assert.Single(server.State.Calls).Method);
    }

    /// <summary>Maps the normalized default timeout to Timeout, preserving DeadlineExceeded.</summary>
    [Fact]
    public async Task AnExpiredTimeoutIsTimeout()
    {
        server.State.HoldSecondPage = true;
        using UtxorpcClient client = new(new UtxorpcClientOptions { Url = server.Url, MaxAttempts = 1, DefaultTimeout = TimeSpan.FromMilliseconds(200) });
        await WarmConnectionAsync(client);
        UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(() => Read(client.V1Beta.Query.SearchAllUtxosAsync(new(), cancellationToken: cancellation.Token)));
        Assert.Equal(UtxorpcErrorKind.Timeout, error.Kind);
        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
        Assert.Equal("", error.Detail);
        Assert.Equal("Timeout (DeadlineExceeded)", error.Message);
    }

    /// <summary>Retries only transport failures, never a response the client cannot read.</summary>
    [Theory]
    [InlineData(StatusCode.Unavailable, 0, true)]
    [InlineData(StatusCode.Internal, 1, true)]
    [InlineData(StatusCode.Internal, 0, false)]
    [InlineData(StatusCode.Cancelled, 0, false)]
    [InlineData(StatusCode.DeadlineExceeded, 0, false)]
    [InlineData(StatusCode.Unknown, 0, false)]
    [InlineData(StatusCode.ResourceExhausted, 0, false)]
    [InlineData(StatusCode.Unimplemented, 0, false)]
    [InlineData(StatusCode.Unavailable, 2, false)]
    [InlineData(StatusCode.Internal, 3, false)]
    public void IsTransient(StatusCode status, int cause, bool expected)
    {
        Exception? debug = cause switch
        {
            1 => new IOException("connection aborted"),
            2 => Unreadable(),
            3 => new IOException("read failed", Unreadable()),
            _ => null
        };
        Assert.Equal(expected, UtxorpcException.IsTransient(new RpcException(new Status(status, "", debug))));
        if (status == StatusCode.Unavailable && cause == 0)
            Assert.Throws<ArgumentNullException>(() => UtxorpcException.IsTransient(null!));

        static InvalidProtocolBufferException Unreadable() => Assert.Throws<InvalidProtocolBufferException>(() =>
            Google.Protobuf.WellKnownTypes.Empty.Parser.ParseFrom(new byte[] { 0x80 }));
    }

    /// <summary>Recognizes both a transport-aborted stream and a server-sent Unavailable.</summary>
    [Fact]
    public async Task ABrokenConnectionIsUnavailable()
    {
        using UtxorpcClient client = new(server.Url);
        foreach (bool abort in new[] { true, false })
        {
            server.State.AbortAfterFirstMessage = abort;
            server.State.FailAfterFirstMessage = !abort;
            UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(() => Read(client.V1Beta.Sync.FollowTipAsync(cancellationToken: cancellation.Token)));
            Assert.Equal(UtxorpcErrorKind.Unavailable, error.Kind);
            Assert.Equal(abort ? StatusCode.Internal : StatusCode.Unavailable, error.StatusCode);
            Assert.True(UtxorpcException.IsTransient(error));
            Assert.True(UtxorpcException.IsTransient(Assert.IsType<RpcException>(error.InnerException)));
        }
    }

    private static async Task Read<T>(IAsyncEnumerable<T> stream)
    {
        await foreach (T _ in stream) { }
    }
}
