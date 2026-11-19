using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Utxorpc.Sdk.Analyzers;

/// <summary>Flags generated calls whose request argument shows that the caller meant the short method.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GeneratedCallAnalyzer : DiagnosticAnalyzer
{
    /// <summary>A null or default request.</summary>
    public const string NullRequestId = "UTXORPC001";
    /// <summary>A bare new(...) with arguments.</summary>
    public const string BareNewId = "UTXORPC002";

    private static readonly DiagnosticDescriptor NullRequest = new(NullRequestId,
        "Generated call with a null request",
        "'{0}' with a null or default request argument is the generated call and throws ArgumentNullException; use the short method",
        "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: "https://github.com/utxorpc/dotnet-sdk#short-methods");

    private static readonly DiagnosticDescriptor BareNew = new(BareNewId,
        "Generated call with a bare new(...)",
        "'{0}' with a bare new(...) binds to the generated call, so new(...) makes its request, not the short method's input; name the input type",
        "Usage", DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: "https://github.com/utxorpc/dotnet-sdk#short-methods");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(NullRequest, BareNew);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        if (!arguments.Any(argument => IsNull(Unwrap(argument.Expression)) || IsBareNew(Unwrap(argument.Expression)))) return;

        SymbolInfo info = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        IMethodSymbol? generated = info.Symbol as IMethodSymbol;
        if (generated is null)
        {
            foreach (ISymbol candidate in info.CandidateSymbols)
            {
                if (candidate is IMethodSymbol method && IsGeneratedClient(method.ContainingType)) { generated = method; break; }
            }
        }
        if (generated is null || !IsGeneratedClient(generated.ContainingType) || generated.Parameters.Length == 0) return;

        // The request is the generated method's first parameter, however the call names or orders its arguments.
        string name = generated.Parameters[0].Name;
        ArgumentSyntax? request = arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == name)
            ?? (arguments[0].NameColon is null ? arguments[0] : null);
        if (request is null) return;
        ExpressionSyntax value = Unwrap(request.Expression);
        if (IsNull(value)) context.ReportDiagnostic(Diagnostic.Create(NullRequest, request.GetLocation(), generated.Name));
        else if (IsBareNew(value)) context.ReportDiagnostic(Diagnostic.Create(BareNew, request.GetLocation(), generated.Name));
    }

    /// <summary>Looks through parentheses and the null-forgiving operator.</summary>
    public static ExpressionSyntax Unwrap(ExpressionSyntax value)
    {
        while (value is ParenthesizedExpressionSyntax or PostfixUnaryExpressionSyntax)
        {
            value = value is ParenthesizedExpressionSyntax parenthesized ? parenthesized.Expression : ((PostfixUnaryExpressionSyntax)value).Operand;
        }
        return value;
    }

    private static bool IsNull(ExpressionSyntax value) =>
        value.IsKind(SyntaxKind.NullLiteralExpression) || value.IsKind(SyntaxKind.DefaultLiteralExpression) || value is DefaultExpressionSyntax;

    private static bool IsBareNew(ExpressionSyntax value) => value is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 };

    // The clients the SDK hands out: the Spec's generated gRPC clients.
    private static bool IsGeneratedClient(INamedTypeSymbol? type)
    {
        if (type is null || !type.ContainingNamespace.ToDisplayString().StartsWith("Utxorpc.V1", System.StringComparison.Ordinal)) return false;
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "ClientBase" && current.ContainingNamespace.ToDisplayString() == "Grpc.Core") return true;
        }
        return false;
    }
}
