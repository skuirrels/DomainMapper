using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Configures an explicit mechanical update policy for one existing-target collection member.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapCollectionAttribute(string targetMember, CollectionUpdatePolicy policy) : Attribute
{
    /// <summary>The target collection property or field name.</summary>
    public string TargetMember { get; } = targetMember;

    /// <summary>The generated collection update operation.</summary>
    public CollectionUpdatePolicy Policy { get; } = policy;
}
