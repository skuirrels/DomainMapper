using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Reuses explicit member bindings from another mapping method in the same mapper.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class IncludeMappingAttribute(string mappingMethod) : Attribute
{
    /// <summary>The base partial mapping method name.</summary>
    public string MappingMethod { get; } = mappingMethod;
}
