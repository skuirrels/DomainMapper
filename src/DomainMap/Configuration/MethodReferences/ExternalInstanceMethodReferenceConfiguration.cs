using DomainMap.Descriptors;
using Microsoft.CodeAnalysis;

namespace DomainMap.Configuration.MethodReferences;

public record ExternalInstanceMethodReferenceConfiguration(string Name, ISymbol TargetMember, INamedTypeSymbol TargetType)
    : IMethodReferenceConfiguration
{
    public string FullName => $"{TargetMember.Name}.{Name}";

    public bool IsExternal => true;

    public INamedTypeSymbol GetTargetType(SimpleMappingBuilderContext ctx) => TargetType;

    public string GetTargetName(SimpleMappingBuilderContext ctx) => TargetMember.Name;

    public override string ToString() => FullName;
}
