using System.Diagnostics;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Mappings.ExistingTarget;

/// <summary>
/// A default implementation of <see cref="IExistingTargetMapping"/>.
/// </summary>
[DebuggerDisplay("{GetType().Name}({SourceType} => {TargetType})")]
public abstract class ExistingTargetMapping : IExistingTargetMapping
{
    protected ExistingTargetMapping(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        Debug.Assert(sourceType.IsNullableUpgraded());
        Debug.Assert(targetType.IsNullableUpgraded());

        SourceType = sourceType;
        TargetType = targetType;
    }

    public ITypeSymbol SourceType { get; }

    public ITypeSymbol TargetType { get; }

    public virtual bool IsSynthetic => false;

    public IEnumerable<TypeMappingKey> BuildAdditionalMappingKeys(TypeMappingConfiguration config) => [];

    public abstract IEnumerable<StatementSyntax> Build(TypeMappingBuildContext ctx, ExpressionSyntax target);
}
