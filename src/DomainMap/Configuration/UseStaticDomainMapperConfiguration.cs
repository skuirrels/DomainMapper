using Microsoft.CodeAnalysis;

namespace DomainMap.Configuration;

public record UseStaticDomainMapperConfiguration(INamedTypeSymbol MapperType);
