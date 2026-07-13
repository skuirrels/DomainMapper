using DomainMap.Descriptors.Constructors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Mappings;

/// <summary>
/// Represents a mapping where the target type has the source as single ctor argument.
/// </summary>
public class CtorMapping(ITypeSymbol sourceType, ITypeSymbol targetType, IInstanceConstructor constructor)
    : NewInstanceMapping(sourceType, targetType)
{
    public override ExpressionSyntax Build(TypeMappingBuildContext ctx) => constructor.CreateInstance(ctx, [ctx.Source]);
}
