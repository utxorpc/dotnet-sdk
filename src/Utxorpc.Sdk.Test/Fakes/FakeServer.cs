using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Sdk;

namespace Utxorpc.Sdk.Test.Fakes;

internal sealed record FakeCall(string Method, IReadOnlyList<KeyValuePair<string, string>> Headers, string ConnectionId);

internal sealed class FakeState
{
    private int callCount;
    /// <summary>Gets the calls received by either version.</summary>
    public ConcurrentQueue<FakeCall> Calls { get; } = new();
    /// <summary>Gets requests received by the fake services.</summary>
    public ConcurrentQueue<IMessage> Requests { get; } = new();
    /// <summary>Gets or sets whether the second search page waits until cancellation.</summary>
    public bool HoldSecondPage { get; set; }
    /// <summary>Gets or sets a status returned instead of an answer.</summary>
    public StatusCode? FailWith { get; set; }
    /// <summary>Gets or sets how many initial calls fail, or null for every call.</summary>
    public int? FailFirstCalls { get; set; }
    /// <summary>Gets or sets whether FollowTip fails after writing its first message.</summary>
    public bool FailAfterFirstMessage { get; set; }
    /// <summary>Gets or sets the number of responses before a stream waits for cancellation.</summary>
    public int StreamMessages { get; set; } = 3;
    /// <summary>Gets or sets whether streams send every known action followed by an unknown one.</summary>
    public bool MixedActions { get; set; }
    /// <summary>Gets or sets the serialized block returned by FetchBlock.</summary>
    public byte[]? BlockBytes { get; set; }
    /// <summary>Gets the completion signalled when a streaming handler exits.</summary>
    public TaskCompletionSource StreamEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Observes stream cancellation once and diagnoses server receipt if it times out.</summary>
    internal async Task WaitForStreamEndAsync(string version)
    {
        TimeSpan wait = TimeSpan.FromSeconds(30);
        long started = Stopwatch.GetTimestamp();
        try
        {
            await StreamEnded.Task.WaitAsync(wait);
        }
        catch (TimeoutException)
        {
            throw new XunitException($"{version}: StreamEnded was not observed after {Stopwatch.GetElapsedTime(started).TotalSeconds:F1} s (limit {wait.TotalSeconds:0} s); server saw the call: {!Calls.IsEmpty}.");
        }
    }

    /// <summary>Records a call and applies the configured failure status.</summary>
    public void Enter(ServerCallContext context)
    {
        KeyValuePair<string, string>[] headers = context.RequestHeaders
            .Select(header => new KeyValuePair<string, string>(header.Key, header.Value)).ToArray();
        int call = Interlocked.Increment(ref callCount);
        Calls.Enqueue(new FakeCall(context.Method, headers, context.GetHttpContext().Connection.Id));
        if (FailWith is not null && (FailFirstCalls is null || call <= FailFirstCalls))
        {
            throw new RpcException(new Status(FailWith.Value, "fake"));
        }
    }
}

internal sealed class FakeServer(WebApplication app, FakeState state, string url) : IAsyncDisposable
{
    /// <summary>Gets the loopback HTTP/2 address selected by Kestrel.</summary>
    public string Url { get; } = url;
    /// <summary>Gets the state shared by the fake service implementations.</summary>
    public FakeState State { get; } = state;

    /// <summary>Starts the fake query, submission, synchronization and watch services.</summary>
    public static async Task<FakeServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        FakeState state = new();
        builder.Services.AddSingleton(state);
        builder.Services.AddGrpc();
        WebApplication app = builder.Build();
        app.MapGrpcService<FakeSyncV1Alpha>();
        app.MapGrpcService<FakeSyncV1Beta>();
        app.MapGrpcService<FakeQueryV1Alpha>();
        app.MapGrpcService<FakeQueryV1Beta>();
        app.MapGrpcService<FakeSubmitV1Alpha>();
        app.MapGrpcService<FakeSubmitV1Beta>();
        app.MapGrpcService<FakeWatchV1Alpha>();
        app.MapGrpcService<FakeWatchV1Beta>();
        await app.StartAsync();
        IServer server = app.Services.GetRequiredService<IServer>();
        string url = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new FakeServer(app, state, url);
    }

    /// <summary>Stops the application and releases its listener.</summary>
    public ValueTask DisposeAsync() => app.DisposeAsync();
}
