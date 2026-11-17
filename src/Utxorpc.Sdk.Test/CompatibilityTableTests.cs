using System.Text.RegularExpressions;
using Google.Protobuf.Reflection;

namespace Utxorpc.Sdk.Test;

/// <summary>Keeps the README's server compatibility table complete and tied to the live tests.</summary>
[Trait("Category", "Unit")]
public sealed partial class CompatibilityTableTests
{
    private static readonly string Live = Path.Combine(AppContext.BaseDirectory, "docs", "live");

    /// <summary>Requires a row for every RPC of the Spec.</summary>
    [Fact]
    public void EveryRpcHasARow()
    {
        foreach (MethodDescriptor rpc in SpecCoverageTests.GeneratedClients()
            .Where(client => client.Namespace!.StartsWith("Utxorpc.V1Beta.", StringComparison.Ordinal))
            .SelectMany(client => SpecCoverageTests.Descriptor(client).Methods))
            Assert.True(CompatibilityTable.Rows.Keys.Any(row => row == rpc.Name || row.StartsWith(rpc.Name + " ", StringComparison.Ordinal)),
                $"The compatibility table has no row for {rpc.Name}.");
    }

    /// <summary>Requires a known mark in every cell, a note line for every note number and a cell for every note line.</summary>
    [Fact]
    public void EveryCellIsAMarkWithItsNote()
    {
        Assert.Equal(2, CompatibilityTable.Servers.Length);
        HashSet<string> used = [];
        foreach ((string row, string[] cells) in CompatibilityTable.Rows)
        {
            Assert.Equal(CompatibilityTable.Servers.Length, cells.Length);
            foreach (string cell in cells)
            {
                Match mark = Cell().Match(cell);
                Assert.True(mark.Success, $"Unknown mark '{cell}' in row '{row}'.");
                if (mark.Groups[2].Success && used.Add(mark.Groups[2].Value))
                    Assert.Contains(CompatibilityTable.Notes, note => note.StartsWith(mark.Groups[2].Value, StringComparison.Ordinal));
            }
        }
        Assert.Equal(used.Count, CompatibilityTable.Notes.Length);
    }

    /// <summary>Pins what each mark allows a live run to see: a time-out passes only where the table warns of one.</summary>
    [Theory]
    [InlineData("✅", Seen.Works, true)]
    [InlineData("✅", Seen.TimedOut, false)]
    [InlineData("✅", Seen.Fails, false)]
    [InlineData("⏳", Seen.Works, true)]
    [InlineData("⏳", Seen.TimedOut, true)]
    [InlineData("⏳", Seen.Fails, false)]
    [InlineData("❌", Seen.Fails, true)]
    [InlineData("❌", Seen.TimedOut, false)]
    [InlineData("❌", Seen.Works, false)]
    [InlineData("—", Seen.Works, false)]
    [InlineData("—", Seen.Fails, false)]
    public void AMarkAllowsOnlyWhatItSays(string mark, Seen seen, bool allowed) => Assert.Equal(allowed, CompatibilityTable.Allows(mark, seen));

    /// <summary>Requires a live check behind every result the table gives, and a row behind every live check.</summary>
    [Fact]
    public void LiveTestsCheckEveryResult()
    {
        HashSet<string> everywhere = Checks("LiveTests.cs"), dingoOnly = Checks("DingoTests.cs");
        dingoOnly.ExceptWith(everywhere);
        int dolos = Array.FindIndex(CompatibilityTable.Servers, header => header.StartsWith("Dolos", StringComparison.Ordinal));
        foreach ((string row, string[] cells) in CompatibilityTable.Rows)
        {
            bool tested = everywhere.Contains(row) || dingoOnly.Contains(row);
            Assert.True(tested == cells.Any(cell => cell != "—"),
                tested ? $"Row '{row}' is checked live but gives no result." : $"Row '{row}' gives a result no live test checks.");
            if (dingoOnly.Contains(row)) Assert.Equal("—", cells[dolos]);
        }
        Assert.All(everywhere.Concat(dingoOnly), row => Assert.True(CompatibilityTable.Rows.ContainsKey(row), $"A live test checks '{row}', which is no row."));
        static HashSet<string> Checks(string file) => Check().Matches(File.ReadAllText(Path.Combine(Live, file)))
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex("^(✅|⏳|❌|—)(?: ([¹²³⁴⁵⁶⁷⁸⁹]))?$")]
    private static partial Regex Cell();

    [GeneratedRegex("\\.Check\\(\"([^\"]+)\"")]
    private static partial Regex Check();
}
