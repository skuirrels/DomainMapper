using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Explicitly excludes one source member from source-completeness validation.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class IgnoreSourceMemberAttribute(string memberName) : Attribute
{
    /// <summary>The source property or field name.</summary>
    public string MemberName { get; } = memberName;

    /// <summary>An optional reviewable explanation.</summary>
    public string? Reason { get; set; }
}
