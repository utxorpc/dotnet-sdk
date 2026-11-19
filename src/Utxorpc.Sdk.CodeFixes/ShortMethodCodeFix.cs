using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Utxorpc.Sdk.Analyzers;

namespace Utxorpc.Sdk.CodeFixes;

/// <summary>Rewrites a flagged generated call to the short method of the same name.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ShortMethodCodeFix)), Shared]
public sealed class ShortMethodCodeFix : CodeFixProvider
{
    private const string SdkAssembly = "Utxorpc.Sdk";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(GeneratedCallAnalyzer.NullRequestId, GeneratedCallAnalyzer.BareNewId);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) return;
        Diagnostic diagnostic = context.Diagnostics[0];
        if (root.FindNode(diagnostic.Location.SourceSpan) is not ArgumentSyntax argument ||
            argument.Parent?.Parent is not InvocationExpressionSyntax invocation) return;

        InvocationExpressionSyntax? replacement = diagnostic.Id == GeneratedCallAnalyzer.NullRequestId
            ? invocation.WithArgumentList(invocation.ArgumentList.WithArguments(invocation.ArgumentList.Arguments.Remove(argument)))
            : WithNamedInput(context.Document, model, invocation, argument, context.CancellationToken);
        // Offered only when the rewritten call binds to a short method of the SDK.
        if (replacement is null ||
            model.GetSpeculativeSymbolInfo(invocation.SpanStart, replacement, SpeculativeBindingOption.BindAsExpression).Symbol
                is not IMethodSymbol { ContainingAssembly.Name: SdkAssembly }) return;
        SyntaxNode changedRoot = root.ReplaceNode(invocation, replacement.WithTriviaFrom(invocation));
        Document changedDocument = context.Document.WithSyntaxRoot(changedRoot);
        SemanticModel? changedModel = await changedDocument.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (changedModel is null) return;
        ImmutableArray<Diagnostic> originalErrors = model.GetDiagnostics(cancellationToken: context.CancellationToken);
        int delta = changedRoot.FullSpan.Length - root.FullSpan.Length;
        // Check uses of the changed return type too, without rejecting unrelated existing errors.
        foreach (Diagnostic error in changedModel.GetDiagnostics(cancellationToken: context.CancellationToken))
        {
            if (error.Severity != DiagnosticSeverity.Error) continue;
            TextSpan span = error.Location.SourceSpan;
            if (span.End > invocation.FullSpan.Start)
            {
                if (span.Start < invocation.FullSpan.End + delta) return;
                span = new TextSpan(span.Start - delta, span.Length);
            }
            if (!originalErrors.Any(original => original.Severity == DiagnosticSeverity.Error &&
                original.Id == error.Id && original.GetMessage() == error.GetMessage() && original.Location.SourceSpan == span)) return;
        }
        context.RegisterCodeFix(CodeAction.Create("Use the short method",
            _ => Task.FromResult(changedDocument), equivalenceKey: diagnostic.Id), diagnostic);
    }

    // X(new(a, b)) becomes X(new T(a, b)), T being the first input of an SDK short method named X with such a constructor.
    private static InvocationExpressionSyntax? WithNamedInput(Document document, SemanticModel model, InvocationExpressionSyntax invocation, ArgumentSyntax argument, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access ||
            GeneratedCallAnalyzer.Unwrap(argument.Expression) is not ImplicitObjectCreationExpressionSyntax creation ||
            model.GetTypeInfo(access.Expression, cancellationToken).Type is not ITypeSymbol receiver) return null;
        foreach (IMethodSymbol method in model.LookupSymbols(invocation.SpanStart, receiver, access.Name.Identifier.ValueText, includeReducedExtensionMethods: true).OfType<IMethodSymbol>())
        {
            if (method.ContainingAssembly.Name != SdkAssembly || method.Parameters.Length == 0) continue;
            ITypeSymbol input = method.Parameters[0].Type;
            if (input is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) input = nullable.TypeArguments[0];
            if (input is not INamedTypeSymbol named) continue;
            TypeSyntax type = (TypeSyntax)SyntaxGenerator.GetGenerator(document).TypeExpression(named);
            ObjectCreationExpressionSyntax explicitCreation = SyntaxFactory.ObjectCreationExpression(
                creation.NewKeyword.WithTrailingTrivia(SyntaxFactory.Space), type.WithAdditionalAnnotations(Simplifier.Annotation), creation.ArgumentList, creation.Initializer);
            // The input type must have a constructor these arguments bind to.
            if (model.GetSpeculativeSymbolInfo(creation.SpanStart, explicitCreation, SpeculativeBindingOption.BindAsExpression).Symbol is not IMethodSymbol) continue;
            return invocation.ReplaceNode(argument, argument.WithExpression(explicitCreation.WithTriviaFrom(argument.Expression)));
        }
        return null;
    }
}
