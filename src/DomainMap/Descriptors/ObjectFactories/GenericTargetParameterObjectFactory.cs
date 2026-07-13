using DomainMap.Emit.Syntax;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Descriptors.ObjectFactories;

/// <summary>
/// A generic object factory which receives mapped source members as parameters and returns the generic target type.
/// Example signature: <c>T Create&lt;T&gt;(SourceMemberType sourceMember);</c>
/// </summary>
public class GenericTargetParameterObjectFactory(GenericTypeChecker typeChecker, SymbolAccessor symbolAccessor, IMethodSymbol method)
    : ParameterObjectFactory(symbolAccessor, method)
{
    public override bool CanCreateInstanceOfType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate) =>
        typeChecker.CheckTypes((Method.TypeParameters[0], targetTypeToCreate));

    protected override ExpressionSyntax BuildCreateType(
        ITypeSymbol sourceType,
        ITypeSymbol targetTypeToCreate,
        ExpressionSyntax source,
        IEnumerable<ArgumentSyntax> arguments
    )
    {
        var methodSyntax = GenericName(Method.Name)
            .WithTypeArgumentList(SyntaxFactoryHelper.TypeArgumentList([NonNullableIdentifier(targetTypeToCreate)]));
        return Invocation(methodSyntax, arguments);
    }
}
