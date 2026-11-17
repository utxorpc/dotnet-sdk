using Grpc.Net.Client;

namespace Utxorpc.Sdk;

/// <summary>Configures a client that creates its own channel.</summary>
/// <remarks>Default retry covers unary calls that fail with Unavailable, never SubmitTx or server streams.
/// The caller handles reconnecting and, where the RPC supports it, resuming from a saved point.</remarks>
public sealed class UtxorpcClientOptions
{
    /// <summary>Gets or sets the server URL.</summary>
    public string? Url { get; set; }

    /// <summary>Gets the headers sent with every call; names that differ only in case are one header, as in gRPC.</summary>
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the total attempts per retried unary call, from 1 (no retry) to 5; defaults to 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the silence before an active connection is pinged; defaults to 60 seconds, or infinite to disable.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets the time each unary call without its own deadline may take, retries included; defaults to infinite (no timeout). Server streams never get it.</summary>
    public TimeSpan DefaultTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>Gets or sets a callback that can change or replace the channel defaults.</summary>
    public Action<GrpcChannelOptions>? ConfigureChannel { get; set; }
}
