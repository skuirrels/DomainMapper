using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Configures null behavior for one explicitly named target member.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapNullAttribute(string targetMember, NullMemberBehavior behavior) : Attribute
{
    /// <summary>The target property or field name.</summary>
    public string TargetMember { get; } = targetMember;

    /// <summary>The generated null behavior.</summary>
    public NullMemberBehavior Behavior { get; } = behavior;
}
