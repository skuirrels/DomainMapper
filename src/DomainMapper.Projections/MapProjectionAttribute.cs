using System.Diagnostics;

namespace DomainMapper.Projections;

/// <summary>Declares a cached, provider-neutral projection for an eligible in-memory mapping contract.</summary>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapProjectionAttribute(string mappingMethod) : Attribute
{
    /// <summary>The source mapping method whose compile-time contract is projected.</summary>
    public string MappingMethod { get; } = mappingMethod;
}
