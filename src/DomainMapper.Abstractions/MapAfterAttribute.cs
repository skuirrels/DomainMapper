using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Binds a typed static completion hook to one mapping method.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapAfterAttribute(string mappingMethod) : Attribute
{
    /// <summary>The partial mapping method name.</summary>
    public string MappingMethod { get; } = mappingMethod;
}
