using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Explicitly excludes one target member from a mapping method.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class IgnoreTargetMemberAttribute(string memberName) : Attribute
{
    /// <summary>The target property or field name.</summary>
    public string MemberName { get; } = memberName;

    /// <summary>An optional reviewable explanation.</summary>
    public string? Reason { get; set; }
}
