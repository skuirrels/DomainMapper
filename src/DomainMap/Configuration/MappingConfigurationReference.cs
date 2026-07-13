using Microsoft.CodeAnalysis;

namespace DomainMap.Configuration;

public record struct MappingConfigurationReference(IMethodSymbol? Method, ITypeSymbol Source, ITypeSymbol Target);
