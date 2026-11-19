using Google.Protobuf;
using Utxorpc.Sdk.Test.Fakes;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks golden bytes through each version's actual gRPC transport.</summary>
[Trait("Category", "Unit")]
public sealed class VectorTransportTests
{
    /// <summary>Preserves a block produced by the independent Rust implementation.</summary>
    [Theory]
    [InlineData("v1alpha", "conway-v11")]
    [InlineData("v1beta", "conway-v11")]
    public async Task FetchBlockPreservesVector(string version, string name)
    {
        byte[] raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "vectors", "cardano", version, $"{name}.pb"));
        await using FakeServer server = await FakeServer.StartAsync();
        server.State.BlockBytes = raw;
        using UtxorpcClient client = new(server.Url);
        IMessage block = version == "v1alpha"
            ? (await client.V1Alpha.Sync.FetchBlockAsync(new())).Block[0].Cardano
            : (await client.V1Beta.Sync.FetchBlockAsync(new())).Block[0].Cardano;

        Assert.Equal(raw, block.ToByteArray());
    }
}
