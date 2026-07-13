using DomainMap.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;

namespace DomainMap.Descriptors.ObjectFactories;

public class GenericSourceTargetObjectFactory(
    GenericTypeChecker typeChecker,
    SymbolAccessor symbolAccessor,
    IMethodSymbol method,
    int sourceTypeParameterIndex
) : ObjectFactory(symbolAccessor, method)
{
    private readonly int _targetTypeParameterIndex = (sourceTypeParameterIndex + 1) % 2;

    public override bool CanCreateInstanceOfType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate) =>
        typeChecker.CheckTypes(
            (Method.TypeParameters[sourceTypeParameterIndex], sourceType),
            (Method.TypeParameters[_targetTypeParameterIndex], targetTypeToCreate)
        );

    protected override ExpressionSyntax BuildCreateType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate, ExpressionSyntax source)
    {
        var typeParams = new TypeSyntax[2];
        typeParams[sourceTypeParameterIndex] = NonNullableIdentifier(sourceType);
        typeParams[_targetTypeParameterIndex] = NonNullableIdentifier(targetTypeToCreate);
        return GenericInvocationWithoutIndention(Method.Name, typeParams, source);
    }
}
