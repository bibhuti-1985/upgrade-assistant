﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.DotNet.UpgradeAssistant.Extensions.Default.CSharp.Analyzers;
using cs = Microsoft.CodeAnalysis.CSharp;
using vb = Microsoft.CodeAnalysis.VisualBasic;

namespace Microsoft.DotNet.UpgradeAssistant.Extensions.Default.CSharp.CodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, LanguageNames.VisualBasic, Name = "UA0013 CodeFix Provider")]
    public class ApiControllerCodeFixer : CodeFixProvider
    {
        public const string GoodNamespace = "Microsoft.AspNetCore.Mvc";
        public const string GoodClassName = "Controller";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ApiControllerAnalyzer.DiagnosticId);

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

            var node = root.FindNode(context.Span, false, true);

            if (node is null)
            {
                return;
            }

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    CodeFixResources.ApiControllerTitle,
                    cancellationToken => ReplaceBaseClass(context.Document, node, cancellationToken),
                    nameof(CodeFixResources.ApiControllerTitle)),
                context.Diagnostics);
        }

        private async Task<Document> ReplaceBaseClass(Document document, SyntaxNode node, CancellationToken cancellationToken)
        {
            var project = document.Project;
            var slnEditor = new SolutionEditor(project.Solution);

            var docEditor = await slnEditor.GetDocumentEditorAsync(document.Id, cancellationToken).ConfigureAwait(false);

            ReplaceController(node, docEditor);

            return docEditor.GetChangedDocument();
        }

        private static void ReplaceController(SyntaxNode node, DocumentEditor docEditor)
        {
            if (IsQualifiedNameSyntax(node))
            {
                var namespaceIdentifier = docEditor.Generator.IdentifierName(GoodNamespace);
                var classNameIdentifier = docEditor.Generator.IdentifierName(GoodClassName);
                var controllerIdentifierSyntax = docEditor.Generator.QualifiedName(namespaceIdentifier, classNameIdentifier);
                docEditor.ReplaceNode(node, controllerIdentifierSyntax);
            }
            else
            {
                var controllerIdentifierSyntax = docEditor.Generator.IdentifierName($"{GoodNamespace}.{GoodClassName}");
                docEditor.ReplaceNode(node, controllerIdentifierSyntax);
            }
        }

        private static bool IsQualifiedNameSyntax(SyntaxNode node)
        {
            return node.IsKind(vb.SyntaxKind.QualifiedName) || node.IsKind(cs.SyntaxKind.QualifiedName);
        }
    }
}
