using Google.Protobuf;
using SpecAlpha = Utxorpc.V1Alpha.Cardano;
using SpecBeta = Utxorpc.V1Beta.Cardano;

namespace Utxorpc.Sdk.Test;

[Trait("Category", "Unit")]
public class ProtocolVectorTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "protocol-vectors");

    public static IEnumerable<object[]> Vectors() =>
        File.ReadAllLines(Path.Combine(Dir, "vectors.tsv"))
            .Skip(1)
            .Select(line => line.Split('\t'))
            .Select(cols => new object[] { cols[0], ulong.Parse(cols[3]), cols[4], int.Parse(cols[5]), cols[6] });

    [Theory]
    [MemberData(nameof(Vectors))]
    public void DecodesV1Alpha(string name, ulong slot, string hash, int txCount, string firstTxHash)
    {
        var raw = File.ReadAllBytes(Path.Combine(Dir, "v1alpha", $"{name}.pb"));
        var block = SpecAlpha.Block.Parser.ParseFrom(raw);

        Assert.Equal(slot, block.Header.Slot);
        Assert.Equal(hash, Hex(block.Header.Hash));
        Assert.Equal(txCount, block.Body?.Tx.Count ?? 0);
        if (firstTxHash != "-") Assert.Equal(firstTxHash, Hex(block.Body!.Tx[0].Hash));
        Assert.Equal(raw, block.ToByteArray());
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void DecodesV1Beta(string name, ulong slot, string hash, int txCount, string firstTxHash)
    {
        var raw = File.ReadAllBytes(Path.Combine(Dir, "v1beta", $"{name}.pb"));
        var block = SpecBeta.Block.Parser.ParseFrom(raw);

        Assert.Equal(slot, block.Header.Slot);
        Assert.Equal(hash, Hex(block.Header.Hash));
        Assert.Equal(txCount, block.Body?.Tx.Count ?? 0);
        if (firstTxHash != "-") Assert.Equal(firstTxHash, Hex(block.Body!.Tx[0].Hash));
        Assert.Equal(raw, block.ToByteArray());
    }

    private static string Hex(ByteString bytes) => Convert.ToHexString(bytes.ToByteArray()).ToLowerInvariant();
}
