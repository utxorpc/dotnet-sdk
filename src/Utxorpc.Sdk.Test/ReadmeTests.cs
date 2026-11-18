using System.Reflection;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks that the README follows the package version and the compiled samples.</summary>
[Trait("Category", "Unit")]
public sealed class ReadmeTests
{
    private static readonly string Docs = Path.Combine(AppContext.BaseDirectory, "docs");

    /// <summary>Checks that the install commands name this package's version.</summary>
    [Fact]
    public void InstallLinesNameThePackageVersion()
    {
        string version = typeof(UtxorpcClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        string[] readme = File.ReadLines(Path.Combine(Docs, "README.md")).Select(line => line.Trim()).ToArray();
        Assert.Contains($"dotnet add package Utxorpc.Sdk --version {version}", readme);
        Assert.Contains($"dotnet new install Utxorpc.Templates@{version}", readme);
    }

    /// <summary>Checks every C# sample line against the example programs and the compiled samples.</summary>
    [Fact]
    public void EverySampleLineIsCompiled()
    {
        HashSet<string> compiled = Directory.EnumerateFiles(Path.Combine(Docs, "samples"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines).Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);
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
            Assert.True(compiled.Contains(line), $"README sample line is in no example or compiled sample: {line}");
            checkedLines++;
        }
        Assert.True(checkedLines >= 20, $"Expected at least 20 C# sample lines; checked {checkedLines}.");
    }
}
