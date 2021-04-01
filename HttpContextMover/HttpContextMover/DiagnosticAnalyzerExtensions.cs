﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Threading;

namespace HttpContextMover
{
    internal static class DiagnosticAnalyzerExtensions
    {
        public static NameSyntax GetFullName(this NameSyntax nameSyntax)
        {
            while (nameSyntax.Parent is QualifiedNameSyntax qualifiedParent)
            {
                nameSyntax = qualifiedParent;
            }

            return nameSyntax;
        }

        public static bool NameEquals(this IAssemblySymbol? symbol, string name, bool startsWith = true)
        {
            if (symbol is null)
            {
                return false;
            }

            if (startsWith)
            {
                return symbol.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return string.Equals(symbol.Name, name, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void RegisterMemberAccess(this AnalysisContext context, Action<InvocationAnalysisContext> action)
        {
            var operationKinds = new[]
            {
                OperationKind.Invocation,
                OperationKind.SimpleAssignment,
                OperationKind.VariableDeclaration,
                OperationKind.ObjectCreation,
                OperationKind.FieldInitializer,
                OperationKind.FieldReference,
            };

            context.RegisterOperationAction(ctx =>
            {
                ISymbol? symbol = ctx.Operation switch
                {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IPropertyReferenceOperation property => property.Property.Type,
                    IObjectCreationOperation creation => creation.Type,
                    ISimpleAssignmentOperation assignment => assignment.Type,
                    IFieldInitializerOperation fieldInitializer => fieldInitializer.Type,
                    IFieldReferenceOperation fieldRef => fieldRef.Type,
                    IVariableDeclarationOperation variableDeclaration => variableDeclaration.Type,
                    _ => null,
                };

                if (symbol is null)
                {
                    return;
                }

                var location = ctx.Operation.Syntax.GetLocation();
                var newCtx = new InvocationAnalysisContext(symbol, location, ctx.Compilation, ctx.Options, ctx.ReportDiagnostic, ctx.CancellationToken);

                action(newCtx);
            }, operationKinds);

            context.RegisterSymbolAction(ctx =>
            {
                var symbol = ctx.Symbol switch
                {
                    IPropertySymbol property => property.Type,
                    IParameterSymbol parameter => parameter.Type,
                    IMethodSymbol method => method.ReturnsVoid ? null : method.ReturnType,
                    IFieldSymbol field => field.Type,
                    _ => null,
                };

                if (symbol is null)
                {
                    return;
                }

                var location = ctx.Symbol.Locations[0];
                var newCtx = new InvocationAnalysisContext(symbol, location, ctx.Compilation, ctx.Options, ctx.ReportDiagnostic, ctx.CancellationToken);

                action(newCtx);
            }, SymbolKind.Property, SymbolKind.Method, SymbolKind.Parameter, SymbolKind.Field);
        }

        public readonly struct InvocationAnalysisContext
        {
            private readonly Action<Diagnostic> _reportDiagnostic;

            public InvocationAnalysisContext(ISymbol symbol, Location location, Compilation compilation, AnalyzerOptions options, Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
            {
                Symbol = symbol;
                Location = location;
                Options = options;
                Compilation = compilation;
                CancellationToken = cancellationToken;

                _reportDiagnostic = reportDiagnostic;
            }

            public Location Location { get; }

            public AnalyzerOptions Options { get; }

            public ISymbol Symbol { get; }

            public Compilation Compilation { get; }

            public CancellationToken CancellationToken { get; }

            public void ReportDiagnostic(Diagnostic diagnostic) => _reportDiagnostic(diagnostic);
        }
    }
}
