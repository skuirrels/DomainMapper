namespace DomainMap.Symbols;

/// <summary>
/// Well-known mapping method parameters.
/// </summary>
public record MappingMethodParameters(
    MethodParameter Source,
    MethodParameter? Target,
    MethodParameter? ReferenceHandler,
    IReadOnlyCollection<MethodParameter> AdditionalParameters
);
