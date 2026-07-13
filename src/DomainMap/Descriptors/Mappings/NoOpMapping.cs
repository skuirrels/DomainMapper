using DomainMap.Descriptors.Mappings.ExistingTarget;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Mappings;

public class NoOpMapping(ITypeSymbol sourceType, ITypeSymbol targetType) : IExistingTargetMapping
{
    public ITypeSymbol SourceType => sourceType;
    public ITypeSymbol TargetType => targetType;
    public bool IsSynthetic => true;

    public IEnumerable<TypeMappingKey> BuildAdditionalMappingKeys(TypeMappingConfiguration config) => [];

    public IEnumerable<StatementSyntax> Build(TypeMappingBuildContext ctx, ExpressionSyntax target) => [];
}
