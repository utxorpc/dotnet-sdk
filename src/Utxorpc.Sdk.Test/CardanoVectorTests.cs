using System.Collections;
using Google.Protobuf;
using SpecAlpha = Utxorpc.V1Alpha.Cardano;
using SpecBeta = Utxorpc.V1Beta.Cardano;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks both generated message families against independent block vectors.</summary>
[Trait("Category", "Unit")]
public class CardanoVectorTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "vectors", "cardano");

    /// <summary>Supplies every golden vector for both protocol versions.</summary>
    public static IEnumerable<object[]> Vectors()
    {
        foreach (string version in new[] { "v1alpha", "v1beta" })
        {
            foreach (string line in File.ReadAllLines(Path.Combine(Dir, "vectors.tsv")).Skip(1))
            {
                string[] cols = line.Split('\t');
                yield return new object[] { version, cols[0], ulong.Parse(cols[3]), cols[4], int.Parse(cols[5]), cols[6] };
            }
        }
    }

    /// <summary>Checks mapped values and a byte-identical protobuf round trip.</summary>
    [Theory]
    [MemberData(nameof(Vectors))]
    public void Decodes(string version, string name, ulong slot, string hash, int txCount, string firstTxHash)
    {
        byte[] raw = File.ReadAllBytes(Path.Combine(Dir, version, $"{name}.pb"));
        IMessage block = version == "v1alpha" ? SpecAlpha.Block.Parser.ParseFrom(raw) : SpecBeta.Block.Parser.ParseFrom(raw);
        IMessage header = (IMessage)block.Descriptor.FindFieldByName("header").Accessor.GetValue(block);
        IMessage? body = (IMessage?)block.Descriptor.FindFieldByName("body").Accessor.GetValue(block);
        IList? transactions = body is null ? null : (IList)body.Descriptor.FindFieldByName("tx").Accessor.GetValue(body);

        Assert.Equal(slot, (ulong)header.Descriptor.FindFieldByName("slot").Accessor.GetValue(header));
        Assert.Equal(hash, Hex((ByteString)header.Descriptor.FindFieldByName("hash").Accessor.GetValue(header)));
        Assert.Equal(txCount, transactions?.Count ?? 0);
        if (firstTxHash != "-")
        {
            IMessage firstTx = (IMessage)transactions![0]!;
            Assert.Equal(firstTxHash, Hex((ByteString)firstTx.Descriptor.FindFieldByName("hash").Accessor.GetValue(firstTx)));
        }
        Assert.Equal(raw, block.ToByteArray());
    }

    private static string Hex(ByteString bytes) => Convert.ToHexString(bytes.Span).ToLowerInvariant();
}
