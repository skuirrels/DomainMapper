namespace DomainMapper.Abstractions;

/// <summary>
/// Defines which side of a mapping must be completely accounted for at compile time.
/// </summary>
public enum MappingCompleteness
{
    /// <summary>Every eligible target member must be mapped or explicitly ignored.</summary>
    Target,

    /// <summary>Every eligible source member must be consumed or explicitly ignored.</summary>
    Source,

    /// <summary>Both source and target completeness are enforced.</summary>
    Both,

    /// <summary>No completeness validation is performed. This is an explicit compatibility escape hatch.</summary>
    None,
}
