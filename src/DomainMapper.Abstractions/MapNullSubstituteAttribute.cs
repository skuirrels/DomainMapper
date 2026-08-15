using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Substitutes a compile-time constant when one nullable source member is null.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapNullSubstituteAttribute(string targetMember, object? value) : Attribute
{
    /// <summary>The target property or field name.</summary>
    public string TargetMember { get; } = targetMember;

    /// <summary>The constant value assigned when the source is null.</summary>
    public object? Value { get; } = value;
}
