using System.Globalization;
using System.Security.Cryptography;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks the independently recorded provenance of every Cardano vector.</summary>
[Trait("Category", "Unit")]
public sealed class VectorManifestTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "vectors", "cardano");
    private static string[][] Rows() => File.ReadLines(Path.Combine(Root, "manifest.tsv"))
        .Skip(1).Select(line => line.Split('\t')).ToArray();

    /// <summary>Checks all source hashes and byte sizes without regenerating expected values.</summary>
    [Fact]
    public void EveryListedFileMatchesItsHashAndSize()
    {
        string[][] rows = Rows();
        Assert.Equal(15, rows.Length);
        foreach (string[] row in rows)
        {
            Assert.Equal(7, row.Length);
            string path = Path.Combine(Root, row[0]);
            Assert.True(File.Exists(path), $"Missing vector: {row[0]}");
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(long.Parse(row[2], CultureInfo.InvariantCulture), bytes.LongLength);
            Assert.Equal(row[1], Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }

    /// <summary>Rejects any copied vector whose provenance has not been recorded.</summary>
    [Fact]
    public void NoUnlistedFiles()
    {
        HashSet<string> listed = Rows().Select(row => row[0]).ToHashSet(StringComparer.Ordinal);
        listed.UnionWith(["manifest.tsv", "LICENSE", "README.md"]);
        foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');
            Assert.True(listed.Contains(relative), $"Unlisted vector: {relative}");
        }
    }
}
