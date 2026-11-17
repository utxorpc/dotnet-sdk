using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Utxorpc.Sdk.Analyzers;
using Utxorpc.Sdk.CodeFixes;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks warnings and safe rewrites of generated calls in both versions.</summary>
[Trait("Category", "Unit")]
public sealed class AnalyzerTests
{
    /// <summary>Flags only ambiguous generated requests and offers only compiling short-method fixes.</summary>
    [Theory]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(default);", "UTXORPC001",
        "await client.V1Beta.Sync.ReadTipAsync();")]
    [InlineData("await client.V1Beta.Query.ReadParamsAsync(null);", "UTXORPC001",
        "await client.V1Beta.Query.ReadParamsAsync();")]
    [InlineData("await client.V1Alpha.Sync.ReadTipAsync(default, cancellationToken: ct);", "UTXORPC001",
        "await client.V1Alpha.Sync.ReadTipAsync(cancellationToken: ct);")]
    [InlineData("await client.V1Beta.Sync.FetchBlockAsync(new(slot, hash));", "UTXORPC002",
        "await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(slot, hash));")]
    [InlineData("await client.V1Beta.Sync.FetchBlockAsync(new(slot, hash), cancellationToken: ct);", "UTXORPC002",
        "await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(slot, hash), cancellationToken: ct);")]
    [InlineData("await client.V1Beta.Query.ReadTxAsync(null);", "UTXORPC001", null)]
    [InlineData("client.V1Beta.Sync.FollowTip(null!);", "UTXORPC001", null)]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(new());", null, null)]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(ct);", null, null)]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(default(CancellationToken));", null, null)]
    [InlineData("await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(slot, hash));", null, null)]
    [InlineData("await client.V1Beta.Sync.FetchBlockAsync(new() { FieldMask = null });", null, null)]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(request: null!);", "UTXORPC001",
        "await client.V1Beta.Sync.ReadTipAsync();")]
    [InlineData("await client.V1Alpha.Sync.ReadTipAsync(request: default);", "UTXORPC001",
        "await client.V1Alpha.Sync.ReadTipAsync();")]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(cancellationToken: ct, request: null);", "UTXORPC001",
        "await client.V1Beta.Sync.ReadTipAsync(cancellationToken: ct);")]
    [InlineData("await client.V1Alpha.Query.ReadParamsAsync(cancellationToken: default, request: new());", null, null)]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(new(), cancellationToken: default);", null, null)]
    [InlineData("await client.V1Alpha.Sync.FetchBlockAsync(request: new(slot, hash));", "UTXORPC002", null)]
    [InlineData("await client.V1Beta.Query.ReadTxAsync(request: null);", "UTXORPC001", null)]
    [InlineData("await client.V1Beta.Sync.FetchBlockAsync(new(123.4, hash));", "UTXORPC002", null)]
    [InlineData("await client.V1Beta.Sync.FetchBlockAsync((new(slot, hash))!);", "UTXORPC002",
        "await client.V1Beta.Sync.FetchBlockAsync(new BlockPoint(slot, hash));")]
    [InlineData("await client.V1Beta.Sync.FetchBlockAsync(new(default(Utxorpc.V1Beta.Sync.FetchBlockRequest)));", "UTXORPC002", null)]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(default).ResponseAsync;", "UTXORPC001", null)]
    [InlineData("await client.V1Alpha.Query.ReadParamsAsync(null).ResponseAsync;", "UTXORPC001", null)]
    [InlineData("Grpc.Core.AsyncUnaryCall<Utxorpc.V1Beta.Sync.ReadTipResponse> raw = client.V1Beta.Sync.ReadTipAsync(default);", "UTXORPC001", null)]
    [InlineData("await client.V1Beta.Sync.FetchBlockAsync(new(slot, hash) { FieldMask = null });", "UTXORPC002", null)]
    [InlineData("await client.V1Beta.Query.ReadParamsAsync(new(null) { FieldMask = new() });", "UTXORPC002", null)]
    [InlineData("var raw = client.V1Beta.Sync.ReadTipAsync(default); _ = await raw.ResponseHeadersAsync;", "UTXORPC001", null)]
    [InlineData("var raw = client.V1Alpha.Query.ReadParamsAsync(null); _ = await raw.ResponseHeadersAsync;", "UTXORPC001", null)]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(null, ct);", "UTXORPC001",
        "await client.V1Beta.Sync.ReadTipAsync(ct);")]
    [InlineData("await client.V1Alpha.Sync.FetchBlockAsync(new(slot, hash));", "UTXORPC002",
        "await client.V1Alpha.Sync.FetchBlockAsync(new BlockPoint(slot, hash));")]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(request: (default(Utxorpc.V1Beta.Sync.ReadTipRequest))!);", "UTXORPC001",
        "await client.V1Beta.Sync.ReadTipAsync();")]
    [InlineData("await client.V1Beta.Sync.ReadTipAsync(default); _ = missing;", "UTXORPC001",
        "await client.V1Beta.Sync.ReadTipAsync(); _ = missing;", "CS0103")]
    [InlineData("_ = missing; await client.V1Beta.Sync.ReadTipAsync(default);", "UTXORPC001",
        "_ = missing; await client.V1Beta.Sync.ReadTipAsync();", "CS0103")]
    [InlineData("_ = string.IsNullOrEmpty(null);", null, null)]
    [InlineData("await client.V1Beta.Query.ReadTxAsync(new(hash));", "UTXORPC002",
        "await client.V1Beta.Query.ReadTxAsync(new System.ReadOnlyMemory<byte>(hash));")]
    [InlineData("await client.V1Beta.Query.ReadParamsAsync(null, null);", "UTXORPC001", null)]
    public async Task FlagsAndFixes(string call, string? expectedId, string? expectedCall, string? expectedCompilerError = null)
    {
        using AdhocWorkspace workspace = new(MefHostServices.Create(
            [typeof(Workspace).Assembly, typeof(CSharpCompilation).Assembly, Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces")]));
        Document document = CreateDocument(workspace, call);
        Compilation compilation = (await document.Project.GetCompilationAsync())!;
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers([new GeneratedCallAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        if (expectedId is null)
        {
            Assert.Empty(diagnostics);
            Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            return;
        }

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        if (expectedId == "UTXORPC001") Assert.Contains("request argument", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.IsType<ArgumentSyntax>((await document.GetSyntaxRootAsync())!.FindNode(diagnostic.Location.SourceSpan));
        List<CodeAction> actions = [];
        await new ShortMethodCodeFix().RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        if (expectedCall is null)
        {
            Assert.Empty(actions);
            return;
        }

        CodeAction fix = Assert.Single(actions);
        Assert.Equal("Use the short method", fix.Title);
        ImmutableArray<CodeActionOperation> operations = await fix.GetOperationsAsync(CancellationToken.None);
        Solution changed = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution;
        Document fixedDocument = await Simplifier.ReduceAsync(changed.GetDocument(document.Id)!);
        SyntaxNode fixedRoot = (await fixedDocument.GetSyntaxRootAsync())!;
        MethodDeclarationSyntax method = Assert.Single(fixedRoot.DescendantNodes().OfType<MethodDeclarationSyntax>());
        Assert.Equal(expectedCall, string.Join(" ", method.Body!.Statements.Select(statement => statement.ToString())));
        Compilation fixedCompilation = (await fixedDocument.Project.GetCompilationAsync())!;
        string[] expectedErrors = expectedCompilerError is null ? [] : [expectedCompilerError];
        Assert.Equal(expectedErrors, fixedCompilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Id));
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string call)
    {
        string source = "using System.Threading; using System.Threading.Tasks; using Utxorpc.Sdk;\n" +
            "static class C { static async Task M(UtxorpcClient client, ulong slot, byte[] hash, CancellationToken ct) { " + call + " } }";
        Project project = workspace.AddProject("CallCheck", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp15))
            .WithMetadataReferences(StreamEventTests.CompilationReferences());
        return project.AddDocument("C.cs", SourceText.From(source));
    }
}
