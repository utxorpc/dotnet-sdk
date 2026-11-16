using System.Collections.Concurrent;
using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Utxorpc.Sdk.Test.Fakes;

internal sealed record FakeCall(string Method, IReadOnlyList<KeyValuePair<string, string>> Headers, string ConnectionId);

internal sealed class FakeState
{
    /// <summary>Gets the calls received by either version.</summary>
    public ConcurrentQueue<FakeCall> Calls { get; } = new();
    /// <summary>Gets or sets a status returned instead of an answer.</summary>
    public StatusCode? FailWith { get; set; }
    /// <summary>Gets or sets the number of responses before a stream waits for cancellation.</summary>
    public int StreamMessages { get; set; } = 3;
    /// <summary>Gets or sets the serialized block returned by FetchBlock.</summary>
    public byte[]? BlockBytes { get; set; }
    /// <summary>Gets the completion signalled when the server sees cancellation.</summary>
    public TaskCompletionSource StreamEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Records a call and applies the configured failure status.</summary>
    public void Enter(ServerCallContext context)
    {
        KeyValuePair<string, string>[] headers = context.RequestHeaders
            .Select(header => new KeyValuePair<string, string>(header.Key, header.Value)).ToArray();
        Calls.Enqueue(new FakeCall(context.Method, headers, context.GetHttpContext().Connection.Id));
        if (FailWith is not null)
        {
            throw new RpcException(new Status(FailWith.Value, "fake"));
        }
    }
}

internal sealed class FakeServer(WebApplication app, FakeState state, string url) : IAsyncDisposable
{
    /// <summary>Gets the loopback HTTP/2 address selected by Kestrel.</summary>
    public string Url { get; } = url;
    /// <summary>Gets the state shared by both generated service implementations.</summary>
    public FakeState State { get; } = state;

    /// <summary>Starts only the two fake synchronization services.</summary>
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
        await app.StartAsync();
        IServer server = app.Services.GetRequiredService<IServer>();
        string url = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new FakeServer(app, state, url);
    }

    /// <summary>Stops the application and releases its listener.</summary>
    public ValueTask DisposeAsync() => app.DisposeAsync();
}
