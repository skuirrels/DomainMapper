using DomainMap.Descriptors;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;

namespace DomainMap.Configuration.MethodReferences;

/// <summary>
/// A reference to an external static method.
/// </summary>
/// <param name="Name">The method name.</param>
/// <param name="TargetType">The type containing the method.</param>
public record ExternalStaticMethodReferenceConfiguration(string Name, INamedTypeSymbol TargetType) : IMethodReferenceConfiguration
{
    public string FullName => $"{TargetType.FullyQualifiedIdentifierName()}.{Name}";

    public bool IsExternal => true;

    public INamedTypeSymbol GetTargetType(SimpleMappingBuilderContext ctx) => TargetType;

    public string? GetTargetName(SimpleMappingBuilderContext ctx) => TargetType.FullyQualifiedIdentifierName();

    public override string ToString() => FullName;
}
