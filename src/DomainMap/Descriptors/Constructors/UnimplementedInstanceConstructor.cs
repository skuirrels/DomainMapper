using DomainMap.Descriptors.Mappings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Descriptors.Constructors;

/// <summary>
/// Emits a non-operational construction expression after a required domain boundary diagnostic.
/// This prevents a suppressed diagnostic from silently bypassing a domain factory.
/// </summary>
internal sealed class UnimplementedInstanceConstructor(ITypeSymbol targetType) : IInstanceConstructor
{
    public bool SupportsObjectInitializer => false;

    public bool SupportsMemberAssignment => false;

    public ExpressionSyntax CreateInstance(
        TypeMappingBuildContext ctx,
        IEnumerable<ArgumentSyntax> args,
        InitializerExpressionSyntax? initializer = null
    ) => PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, DefaultExpression(FullyQualifiedIdentifier(targetType)));
}
