using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Descriptors.ObjectFactories;

/// <summary>
/// An object factory which receives mapped source members as parameters.
/// Example signature: <c>TypeToCreate Create(SourceMemberType sourceMember);</c>
/// </summary>
public class ParameterObjectFactory(SymbolAccessor symbolAccessor, IMethodSymbol method) : ObjectFactory(symbolAccessor, method, true)
{
    public override bool CanCreateInstanceOfType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate) =>
        SymbolEqualityComparer.Default.Equals(Method.ReturnType, targetTypeToCreate);

    protected override ExpressionSyntax BuildCreateType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate, ExpressionSyntax source) =>
        InvocationWithoutIndention(Method.Name);

    protected override ExpressionSyntax BuildCreateType(
        ITypeSymbol sourceType,
        ITypeSymbol targetTypeToCreate,
        ExpressionSyntax source,
        IEnumerable<ArgumentSyntax> arguments
    ) => Invocation(IdentifierName(Method.Name), arguments);

    protected static InvocationExpressionSyntax Invocation(ExpressionSyntax method, IEnumerable<ArgumentSyntax> arguments) =>
        InvocationExpression(method).WithArgumentList(ArgumentListWithoutIndention(arguments));
}
