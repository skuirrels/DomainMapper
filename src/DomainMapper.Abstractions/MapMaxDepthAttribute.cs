using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Enables bounded recursive mapping for one method. Mappings without this attribute reject recursive contracts.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapMaxDepthAttribute(int maximumDepth) : Attribute
{
    /// <summary>The maximum number of mapped object nodes.</summary>
    public int MaximumDepth { get; } = maximumDepth;

    /// <summary>The behavior when the maximum depth is exhausted.</summary>
    public DepthExhaustionBehavior ExhaustionBehavior { get; set; }
}
