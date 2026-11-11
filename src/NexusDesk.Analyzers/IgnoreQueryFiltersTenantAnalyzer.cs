using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexusDesk.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoreQueryFiltersTenantAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ND0001";
    private const string Title = "Direct Unfiltered DbSet Query Prohibited on Tenant-Scoped Entity";
    private const string MessageFormat = "Direct unfiltered query on tenant-scoped entity '{0}' using IgnoreQueryFilters() is prohibited without [AllowCrossTenantQuery]";
    private const string Description = "Calling .IgnoreQueryFilters() on an ITenantScoped entity outside of a class or method explicitly decorated with [AllowCrossTenantQuery] is prohibited.";
    private const string Category = "Security";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if invoking IgnoreQueryFilters
        string? methodName = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
        }
        else if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            methodName = identifier.Identifier.Text;
        }

        if (methodName != "IgnoreQueryFilters")
        {
            return;
        }

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

        ITypeSymbol? entityType = null;
        if (methodSymbol != null)
        {
            if (methodSymbol.TypeArguments.Length > 0)
            {
                entityType = methodSymbol.TypeArguments[0];
            }
            else if (methodSymbol.ReceiverType is INamedTypeSymbol receiverNamedType && receiverNamedType.TypeArguments.Length > 0)
            {
                entityType = receiverNamedType.TypeArguments[0];
            }
            else if (methodSymbol.ReturnType is INamedTypeSymbol returnNamedType && returnNamedType.TypeArguments.Length > 0)
            {
                entityType = returnNamedType.TypeArguments[0];
            }
        }
        else if (invocation.Expression is MemberAccessExpressionSyntax memberAccessExpr)
        {
            var receiverTypeInfo = context.SemanticModel.GetTypeInfo(memberAccessExpr.Expression, context.CancellationToken);
            if (receiverTypeInfo.Type is INamedTypeSymbol namedReceiver && namedReceiver.TypeArguments.Length > 0)
            {
                entityType = namedReceiver.TypeArguments[0];
            }
        }

        if (entityType == null)
        {
            return;
        }

        // Check if entityType implements ITenantScoped
        if (!IsTenantScoped(entityType))
        {
            return;
        }

        // Check if caller or containing method/type has [AllowCrossTenantQuery]
        if (HasAllowCrossTenantQueryAttribute(context))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), entityType.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsTenantScoped(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.Name == "ITenantScoped")
        {
            return true;
        }

        return typeSymbol.AllInterfaces.Any(i =>
            i.Name == "ITenantScoped" ||
            i.ToDisplayString() == "NexusDesk.SharedKernel.Primitives.ITenantScoped");
    }

    private static bool HasAllowCrossTenantQueryAttribute(SyntaxNodeAnalysisContext context)
    {
        var currentSymbol = context.ContainingSymbol;

        while (currentSymbol != null)
        {
            if (currentSymbol.GetAttributes().Any(IsAllowCrossTenantQueryAttribute))
            {
                return true;
            }

            currentSymbol = currentSymbol.ContainingSymbol;
        }

        // Fallback: check syntax nodes upwards for attribute syntax
        var node = context.Node;
        while (node != null)
        {
            if (node is MethodDeclarationSyntax methodDecl)
            {
                if (HasAttributeNamed(methodDecl.AttributeLists, "AllowCrossTenantQuery"))
                {
                    return true;
                }
            }
            else if (node is ClassDeclarationSyntax classDecl)
            {
                if (HasAttributeNamed(classDecl.AttributeLists, "AllowCrossTenantQuery"))
                {
                    return true;
                }
            }
            node = node.Parent;
        }

        return false;
    }

    private static bool IsAllowCrossTenantQueryAttribute(AttributeData attribute)
    {
        var name = attribute.AttributeClass?.Name;
        return name == "AllowCrossTenantQueryAttribute" ||
               name == "AllowCrossTenantQuery" ||
               attribute.AttributeClass?.ToDisplayString() == "NexusDesk.SharedKernel.MultiTenancy.AllowCrossTenantQueryAttribute";
    }

    private static bool HasAttributeNamed(SyntaxList<AttributeListSyntax> attributeLists, string targetName)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var attrName = attr.Name.ToString();
                if (attrName == targetName ||
                    attrName == targetName + "Attribute" ||
                    attrName.EndsWith("." + targetName) ||
                    attrName.EndsWith("." + targetName + "Attribute"))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
