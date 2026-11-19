using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Utxorpc.Sdk.Test.Fakes;

/// <summary>Counts gRPC calls started even when the connection fails before any HTTP request.</summary>
internal sealed class StartedCallCounter : ILoggerFactory, ILogger
{
    private int calls;
    public int Calls => Volatile.Read(ref calls);
    public ILogger CreateLogger(string categoryName) => this;
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (eventId.Name == "StartingCall") Interlocked.Increment(ref calls);
    }
}

/// <summary>Simulates the managed gRPC transport's connect timeout without relying on dropped packets.</summary>
internal sealed class TimedOutConnectTransport() : DelegatingHandler(new SocketsHttpHandler
{
    UseProxy = false,
    ConnectTimeout = TimeSpan.FromMilliseconds(100),
    ConnectCallback = (_, token) => WaitForCancellation(token)
})
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try { return await base.SendAsync(request, cancellationToken); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && exception.InnerException is TimeoutException timeout)
        {
            // A ConnectCallback replaces gRPC's managed transport. Give the call the same failure
            // that transport reports on a real connect timeout; SocketsHttpHandler supplies the timer.
            throw new RpcException(new Status(StatusCode.Unavailable, "Error connecting to subchannel.", timeout));
        }
    }

    public static async ValueTask<Stream> WaitForCancellation(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("A pending connect should only end through cancellation.");
    }
}
