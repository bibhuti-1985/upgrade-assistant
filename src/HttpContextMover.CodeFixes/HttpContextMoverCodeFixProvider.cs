﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HttpContextMover
{
    public abstract class HttpContextMoverCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(HttpContextMoverAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            if (root is null)
            {
                return;
            }

            var diagnostic = context.Diagnostics.First();
            var semantic = await context.Document.GetSemanticModelAsync(context.CancellationToken);

            if (semantic is null)
            {
                return;
            }

            // Find the type declaration identified by the diagnostic.
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            if (semantic.GetOperation(node, context.CancellationToken) is not IPropertyReferenceOperation property)
            {
                return;
            }

            var methodOperation = GetEnclosingMethodOperation(property);

            if (methodOperation is null)
            {
                return;
            }

            //// Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.HttpContextPassthroughCodeFixer,
                    createChangedSolution: c => MakePassHttpContextThrough(context.Document, methodOperation, property, c),
                    equivalenceKey: nameof(CodeFixResources.HttpContextPassthroughCodeFixer)),
                diagnostic);
        }

        private IOperation? GetEnclosingMethodOperation(IOperation? operation)
        {
            while (operation is not null)
            {
                if (IsEnclosedMethodOperation(operation))
                {
                    return operation;
                }

                operation = operation.Parent;
            }

            return default;
        }

        private async Task<Solution> MakePassHttpContextThrough(Document document, IOperation methodOperation, IPropertyReferenceOperation propertyOperation, CancellationToken cancellationToken)
        {
            var slnEditor = new SolutionEditor(document.Project.Solution);
            var editor = await slnEditor.GetDocumentEditorAsync(document.Id, cancellationToken);

            // Add parameter if not available
            var parameter = await AddMethodParameter(editor, document, methodOperation, propertyOperation, cancellationToken);

            if (parameter is null)
            {
                return document.Project.Solution;
            }

            // Update node usage
            var text = editor.Generator.GetName(parameter);
            var name = editor.Generator.IdentifierName(text);

            editor.ReplaceNode(propertyOperation.Syntax, name);

            if (methodOperation.SemanticModel?.GetDeclaredSymbol(methodOperation.Syntax, cancellationToken) is ISymbol methodSymbol)
            {
                await UpdateCallers(methodSymbol, propertyOperation.Property, slnEditor, cancellationToken);
            }

            return slnEditor.GetChangedSolution();
        }

        private async Task<SyntaxNode?> AddMethodParameter(DocumentEditor editor, Document document, IOperation methodOperation, IPropertyReferenceOperation propertyOperation, CancellationToken token)
        {
            // Get the symbol representing the type to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(token);

            if (semanticModel is null)
            {
                return default;
            }

            var symbol = semanticModel.GetDeclaredSymbol(methodOperation.Syntax, token);

            if (symbol is not IMethodSymbol method)
            {
                return null;
            }

            var parameter = method.Parameters.FirstOrDefault(p =>
            {
                if (p.Type is null)
                {
                    return false;
                }

                return SymbolEqualityComparer.IncludeNullability.Equals(p.Type, propertyOperation.Property.Type);
            });

            if (parameter is not null && !parameter.DeclaringSyntaxReferences.IsEmpty)
            {
                return parameter.DeclaringSyntaxReferences[0].GetSyntax(token);
            }

            var propertyTypeSyntaxNode = editor.Generator.NameExpression(propertyOperation.Property.Type);

            if (parameter is null)
            {
                const string CurrentContextName = "currentContext";

                var ps = editor.Generator.GetParameters(methodOperation.Syntax);
                var current = editor.Generator.IdentifierName(CurrentContextName);
                var p = editor.Generator.ParameterDeclaration(CurrentContextName, propertyTypeSyntaxNode);

                editor.AddParameter(methodOperation.Syntax, p);

                return p;
            }

            return null;
        }

        private async Task UpdateCallers(ISymbol methodSymbol, IPropertySymbol property, SolutionEditor slnEditor, CancellationToken token)
        {
            // Check callers
            var callers = await SymbolFinder.FindCallersAsync(methodSymbol, slnEditor.OriginalSolution, token);

            foreach (var caller in callers)
            {
                var location = caller.Locations.FirstOrDefault();

                if (location is null)
                {
                    continue;
                }

                if (!TryGetDocument(slnEditor.OriginalSolution, location.SourceTree, token, out var document))
                {
                    continue;
                }

                var editor = await slnEditor.GetDocumentEditorAsync(document.Id, token);
                var root = await document.GetSyntaxRootAsync(token);

                if (root is null)
                {
                    continue;
                }

                var callerNode = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);

                if (callerNode is null)
                {
                    continue;
                }

                ReplaceMethod(callerNode, editor, property);
            }
        }

        protected abstract void ReplaceMethod(SyntaxNode callerNode, SyntaxEditor editor, IPropertySymbol property);

        protected abstract bool IsEnclosedMethodOperation(IOperation operation);

        private bool TryGetDocument(Solution sln, SyntaxTree? tree, CancellationToken token, [MaybeNullWhen(false)] out Document document)
        {
            if (tree is null)
            {
                document = null;
                return false;
            }

            foreach (var project in sln.Projects)
            {
                var doc = project.GetDocument(tree);

                if (doc is not null)
                {
                    document = doc;
                    return true;
                }
            }

            document = null;
            return false;
        }
    }
}
