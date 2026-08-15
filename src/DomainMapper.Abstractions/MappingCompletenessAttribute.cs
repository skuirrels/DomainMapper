using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Sets the compile-time completeness contract for one mapping method.</summary>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MappingCompletenessAttribute(MappingCompleteness policy) : Attribute
{
    /// <summary>The requested completeness policy.</summary>
    public MappingCompleteness Policy { get; } = policy;
}
