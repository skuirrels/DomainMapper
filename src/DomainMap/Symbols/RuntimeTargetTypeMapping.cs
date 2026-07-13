using DomainMap.Descriptors.Mappings;

namespace DomainMap.Symbols;

public record RuntimeTargetTypeMapping(INewInstanceMapping Mapping, bool IsAssignableToMethodTargetType, bool IsDerivedTypeMapping);
