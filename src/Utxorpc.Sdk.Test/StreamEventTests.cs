using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Utxorpc.Sdk.V1Beta;
using Sync = Utxorpc.V1Beta.Sync;
using Watch = Utxorpc.V1Beta.Watch;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks union exhaustiveness, version parity and hand-made events.</summary>
[Trait("Category", "Unit")]
public sealed class StreamEventTests
{
    /// <summary>Types an internal read cancellation even when the status or trailers are unavailable.</summary>
    [Theory]
    [InlineData(null, StatusCode.Unavailable, UtxorpcErrorKind.Unavailable, true)]
    [InlineData(StatusCode.OK, StatusCode.Unavailable, UtxorpcErrorKind.Unavailable, true)]
    [InlineData(StatusCode.Cancelled, StatusCode.Cancelled, UtxorpcErrorKind.Other, false)]
    public async Task InternallyCancelledReadUsesAvailableStatus(StatusCode? callStatus, StatusCode expectedStatus, UtxorpcErrorKind kind, bool transient)
    {
        AllocationTests.StubChannel channel = new();
        channel.Invoker.Cancelled = true;
        channel.Invoker.StreamStatus = callStatus is StatusCode code ? new Status(code, "fake stream status") : null;
        channel.Invoker.StreamTrailersUnreadable = true;
        using UtxorpcClient client = new(channel);
        UtxorpcException error = await Assert.ThrowsAsync<UtxorpcException>(async () =>
        {
            await foreach (FollowTipEvent _ in client.V1Beta.Sync.FollowTipAsync()) Assert.Fail("The read should fail before yielding an event.");
        });
        Assert.Equal(kind, error.Kind);
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.Equal(transient, UtxorpcException.IsTransient(error));
        Assert.Empty(error.Trailers);
    }

    private static Type[] Unions() => typeof(UtxorpcClient).Assembly.GetExportedTypes()
        .Where(type => type.IsDefined(typeof(UnionAttribute))).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

    private static Type[] Cases(Type union) => union.GetConstructors()
        .Where(constructor => constructor.GetParameters().Length == 1)
        .Select(constructor => constructor.GetParameters()[0].ParameterType).ToArray();

    internal static IEnumerable<MetadataReference> CompilationReferences()
    {
        string? platforms = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        Assert.False(string.IsNullOrEmpty(platforms), "The test host must supply its trusted platform assemblies.");
        return platforms.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
    }

    private static Diagnostic[] Compile(string members)
    {
        CSharpCompilation compilation = CSharpCompilation.Create("UnionCheck",
            [CSharpSyntaxTree.ParseText("#nullable enable\nstatic class C { " + members + " }", new CSharpParseOptions(LanguageVersion.CSharp15))],
            CompilationReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic> { ["CS1701"] = ReportDiagnostic.Suppress, ["CS1702"] = ReportDiagnostic.Suppress }));
        return compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
    }

    private static string Switch(Type union, IEnumerable<Type> cases) =>
        $"static int F(global::{union.FullName} e) => e switch {{ " +
        string.Join(", ", cases.Select((type, index) => $"global::{type.FullName} c{index} => {index}")) + " };";

    /// <summary>Requires every union's complete switch expression to compile without diagnostics.</summary>
    [Fact]
    public void ASwitchOverEveryCaseCompilesClean()
    {
        Type[] unions = Unions();
        Assert.Equal(4, unions.Length);
        foreach (Type union in unions)
        {
            Type[] cases = Cases(union);
            Assert.Equal(3, cases.Length);
            Assert.Empty(Compile(Switch(union, cases)));
        }
    }

    /// <summary>Requires each forgotten case to produce the exhaustiveness warning naming that case.</summary>
    [Fact]
    public void AForgottenCaseIsWarningCS8509()
    {
        Type[] unions = Unions();
        Assert.Equal(4, unions.Length);
        foreach (Type union in unions)
        {
            Type[] cases = Cases(union);
            Assert.Equal(3, cases.Length);
            foreach (Type omitted in cases)
            {
                Diagnostic warning = Assert.Single(Compile(Switch(union, cases.Where(type => type != omitted))));
                Assert.Equal("CS8509", warning.Id);
                Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
                Assert.Contains(omitted.FullName!, warning.GetMessage(), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Requires matching public event types and member signatures in both versions.</summary>
    [Fact]
    public void BothVersionsHaveTheSameEvents()
    {
        Type[] types = typeof(UtxorpcClient).Assembly.GetExportedTypes();
        Type[] alpha = types.Where(type => type.Namespace == "Utxorpc.Sdk.V1Alpha").OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
        Type[] beta = types.Where(type => type.Namespace == "Utxorpc.Sdk.V1Beta").OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(alpha.Select(type => type.Name), beta.Select(type => type.Name));
        for (int i = 0; i < alpha.Length; i++)
        {
            string[] Members(Type type) => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => member.ToString()!.Replace("V1Alpha", "V1Beta", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(Members(alpha[i]), Members(beta[i]));
        }
    }

    /// <summary>Checks conversion, payload identity, null payloads, default events and mutable messages.</summary>
    [Fact]
    public void HandMadeEventsHoldTheirCase()
    {
        Sync.AnyChainBlock block = new();
        Sync.BlockRef reset = new();
        FollowTipEvent[] blocks = [new BlockApplied(block), new BlockUndone(block), new Reset(reset)];
        for (int i = 0; i < blocks.Length; i++)
        {
            FollowTipEvent e = blocks[i];
            Assert.True(e.HasValue);
            Assert.Null(e.Tip);
            int kind = e switch
            {
                BlockApplied applied => Check(block, applied.Block, e.Response.Apply, 0),
                BlockUndone undone => Check(block, undone.Block, e.Response.Undo, 1),
                Reset point => Check(reset, point.Point, e.Response.Reset, 2)
            };
            Assert.Equal(i, kind);
            Assert.Equal(new[] { typeof(BlockApplied), typeof(BlockUndone), typeof(Reset) }[i], e.Value!.GetType());
        }
        Watch.AnyChainTx tx = new();
        Watch.BlockRef idle = new();
        WatchTxEvent[] transactions = [new TxApplied(tx), new TxUndone(tx), new Idle(idle)];
        for (int i = 0; i < transactions.Length; i++)
        {
            WatchTxEvent e = transactions[i];
            Assert.True(e.HasValue);
            int kind = e switch
            {
                TxApplied applied => Check(tx, applied.Tx, e.Response.Apply, 0),
                TxUndone undone => Check(tx, undone.Tx, e.Response.Undo, 1),
                Idle point => Check(idle, point.Point, e.Response.Idle, 2)
            };
            Assert.Equal(i, kind);
            Assert.Equal(new[] { typeof(TxApplied), typeof(TxUndone), typeof(Idle) }[i], e.Value!.GetType());
        }
        Assert.Throws<ArgumentNullException>(() => { FollowTipEvent e = new BlockApplied(null!); });
        Assert.Throws<ArgumentNullException>(() => { FollowTipEvent e = new BlockUndone(null!); });
        Assert.Throws<ArgumentNullException>(() => { FollowTipEvent e = new Reset(null!); });
        Assert.Throws<ArgumentNullException>(() => { WatchTxEvent e = new TxApplied(null!); });
        Assert.Throws<ArgumentNullException>(() => { WatchTxEvent e = new TxUndone(null!); });
        Assert.Throws<ArgumentNullException>(() => { WatchTxEvent e = new Idle(null!); });
        FollowTipEvent empty = default;
        Assert.False(empty.HasValue);
        Assert.Null(empty.Value);
        Assert.Throws<InvalidOperationException>(() => empty.Response);
        Assert.Throws<SwitchExpressionException>(() => Kind(empty));
        FollowTipEvent cleared = new BlockApplied(block);
        cleared.Response.ClearAction();
        Assert.False(cleared.HasValue);
        Assert.Null(cleared.Value);
        Assert.True(cleared is null);

        static int Kind(FollowTipEvent value) => value switch { BlockApplied _ => 0, BlockUndone _ => 1, Reset _ => 2 };

        static int Check(object expected, object payload, object response, int kind)
        {
            Assert.Same(expected, payload);
            Assert.Same(payload, response);
            return kind;
        }
    }
}
