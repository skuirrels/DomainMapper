using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.Constructors;

public interface IParameterMappingInstanceConstructor : IInstanceConstructor
{
    bool SupportsParameterMapping { get; }

    IMethodSymbol ParameterMappingMethod { get; }
}
