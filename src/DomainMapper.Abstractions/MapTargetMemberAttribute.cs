using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Binds a typed mapper-owned static method to one target member of one mapping method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapTargetMemberAttribute(string mappingMethod, string targetMember) : Attribute
{
    /// <summary>The partial mapping method name.</summary>
    public string MappingMethod { get; } = mappingMethod;

    /// <summary>The target property or field name.</summary>
    public string TargetMember { get; } = targetMember;
}
