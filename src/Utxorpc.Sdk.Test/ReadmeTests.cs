using System.Reflection;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks that the README follows the package version and compiled example.</summary>
[Trait("Category", "Unit")]
public sealed class ReadmeTests
{
    private static readonly string Docs = Path.Combine(AppContext.BaseDirectory, "docs");

    /// <summary>Checks that the install command names this package's version.</summary>
    [Fact]
    public void InstallLineNamesThePackageVersion()
    {
        string version = typeof(UtxorpcClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        Assert.Contains($"dotnet add package Utxorpc.Sdk --version {version}",
            File.ReadLines(Path.Combine(Docs, "README.md")).Select(line => line.Trim()));
    }

    /// <summary>Checks every C# sample line against the compiled example.</summary>
    [Fact]
    public void EverySampleLineIsInTheExample()
    {
        HashSet<string> example = File.ReadLines(Path.Combine(Docs, "Program.cs")).Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);
        bool inSample = false;
        int checkedLines = 0;
        foreach (string raw in File.ReadLines(Path.Combine(Docs, "README.md")))
        {
            string line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inSample = line is "```csharp" or "```cs";
                continue;
            }
            if (!inSample || line.Length == 0) continue;
            Assert.True(example.Contains(line), $"README sample line is missing from the example: {line}");
            checkedLines++;
        }
        Assert.True(checkedLines >= 20, $"Expected at least 20 C# sample lines; checked {checkedLines}.");
    }
}
