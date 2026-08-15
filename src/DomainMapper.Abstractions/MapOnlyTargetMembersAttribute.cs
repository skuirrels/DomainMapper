using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Restricts an existing-target mapping to an explicit allow-list. Members not listed are never assigned.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapOnlyTargetMembersAttribute(params string[] memberNames) : Attribute
{
    /// <summary>The only target members that generated code may assign.</summary>
    public string[] MemberNames { get; } = memberNames;
}
