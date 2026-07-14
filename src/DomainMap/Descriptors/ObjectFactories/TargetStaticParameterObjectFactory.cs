using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;

namespace DomainMap.Descriptors.ObjectFactories;

/// <summary>
/// A required target-owned static factory whose parameters are mapped from source members.
/// </summary>
public sealed class TargetStaticParameterObjectFactory(SymbolAccessor symbolAccessor, IMethodSymbol method)
    : ObjectFactory(symbolAccessor, method, mapToParameters: true, isDomainFactory: true)
{
    public override bool CanCreateInstanceOfType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate) =>
        SymbolEqualityComparer.Default.Equals(Method.ReturnType, targetTypeToCreate);

    protected override ExpressionSyntax BuildCreateType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate, ExpressionSyntax source) =>
        InvocationWithoutIndention(MemberAccess(NonNullableIdentifier(Method.ContainingType), Method.Name));

    protected override ExpressionSyntax BuildCreateType(
        ITypeSymbol sourceType,
        ITypeSymbol targetTypeToCreate,
        ExpressionSyntax source,
        IEnumerable<ArgumentSyntax> arguments
    )
    {
        var methodAccess = MemberAccess(NonNullableIdentifier(Method.ContainingType), Method.Name);
        return InvocationWithoutIndention(methodAccess).WithArgumentList(ArgumentListWithoutIndention(arguments));
    }
}
