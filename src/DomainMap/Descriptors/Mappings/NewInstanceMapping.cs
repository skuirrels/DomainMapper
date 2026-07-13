using System.Diagnostics;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Mappings;

/// <inheritdoc cref="INewInstanceMapping"/>
[DebuggerDisplay("{GetType().Name}({SourceType} => {TargetType})")]
public abstract class NewInstanceMapping : INewInstanceMapping
{
    protected NewInstanceMapping(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        Debug.Assert(sourceType.IsNullableUpgraded());
        Debug.Assert(targetType.IsNullableUpgraded());

        SourceType = sourceType;
        TargetType = targetType;
    }

    public ITypeSymbol SourceType { get; }

    public ITypeSymbol TargetType { get; }

    /// <inheritdoc cref="INewInstanceMapping.IsSynthetic"/>
    public virtual bool IsSynthetic => false;

    public virtual IEnumerable<TypeMappingKey> BuildAdditionalMappingKeys(TypeMappingConfiguration config) => [];

    public abstract ExpressionSyntax Build(TypeMappingBuildContext ctx);
}
