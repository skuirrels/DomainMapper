using Microsoft.CodeAnalysis;

namespace DomainMap.Configuration;

public record MapperIgnoreEnumValueConfiguration(IFieldSymbol Value) : MapperIgnoreConfigurationBase;
