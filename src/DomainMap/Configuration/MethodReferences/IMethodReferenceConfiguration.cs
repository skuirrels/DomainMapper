using DomainMap.Descriptors;
using Microsoft.CodeAnalysis;

namespace DomainMap.Configuration.MethodReferences;

public interface IMethodReferenceConfiguration
{
    INamedTypeSymbol? GetTargetType(SimpleMappingBuilderContext ctx);

    string? GetTargetName(SimpleMappingBuilderContext ctx);

    string Name { get; }

    string FullName { get; }

    bool IsExternal { get; }
}
