namespace Utxorpc.Sdk.Test;

/// <summary>What a live test saw of one row: it worked, it gave no answer in the test's time, or it did not work.</summary>
public enum Seen
{
    /// <summary>The call did what the row says.</summary>
    Works,

    /// <summary>The server gave no answer within the test's time; this says nothing about support.</summary>
    TimedOut,

    /// <summary>The server rejected the call, or answered without what the row says.</summary>
    Fails,
}

/// <summary>Reads the README's server compatibility table.</summary>
internal static class CompatibilityTable
{
    private static readonly Lazy<(string[] Servers, Dictionary<string, string[]> Rows, string[] Notes)> Table = new(Read);

    /// <summary>Gets the server column headers, such as "Dolos v1.6.3".</summary>
    internal static string[] Servers => Table.Value.Servers;

    /// <summary>Gets each row's cells by its name, in the order of <see cref="Servers"/>.</summary>
    internal static IReadOnlyDictionary<string, string[]> Rows => Table.Value.Rows;

    /// <summary>Gets the note lines under the table.</summary>
    internal static string[] Notes => Table.Value.Notes;

    /// <summary>Fails unless the row's cell, in the column whose header starts with the server's name, allows what a live run saw.</summary>
    internal static void Check(string row, string server, Seen seen)
    {
        int column = Array.FindIndex(Servers, header => header.StartsWith(server, StringComparison.OrdinalIgnoreCase));
        Assert.True(column >= 0, $"The compatibility table has no {server} column.");
        Assert.True(Rows.TryGetValue(row, out string[]? cells), $"The compatibility table has no row '{row}'.");
        Assert.True(Allows(cells![column].Split(' ')[0], seen),
            $"The README's compatibility table has {cells[column]} for '{row}' on {Servers[column]}; this run saw {seen}. Correct the table if the server changed.");
    }

    /// <summary>Gets whether a mark allows what a live run saw. A time-out is allowed only where the table warns of one.</summary>
    internal static bool Allows(string mark, Seen seen) => mark switch
    {
        "✅" => seen == Seen.Works,
        "⏳" => seen != Seen.Fails,
        "❌" => seen == Seen.Fails,
        _ => false,
    };

    private static (string[] Servers, Dictionary<string, string[]> Rows, string[] Notes) Read()
    {
        string[] section = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "docs", "README.md"))
            .SkipWhile(line => line != "## Server compatibility").Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)).ToArray();
        string[][] lines = section.Where(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim().Replace("`", "")).ToArray()).ToArray();
        Assert.True(lines.Length > 2, "The README has no server compatibility table.");
        return (lines[0][1..], lines.Skip(2).ToDictionary(cells => cells[0], cells => cells[1..], StringComparer.Ordinal),
            section.Select(line => line.TrimStart('-', ' ')).Where(line => line.Length > 0 && "¹²³⁴⁵⁶⁷⁸⁹".Contains(line[0])).ToArray());
    }
}
