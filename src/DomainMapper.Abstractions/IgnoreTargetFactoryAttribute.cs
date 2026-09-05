using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Declares that one mapping method may construct the named target type directly although the type declares a static
/// factory. Suppresses <c>DMPR108</c> for that type within the mapping method and its nested helpers.
/// A declaration whose type the mapping never constructs directly is reported as stale.
/// </summary>
/// <example>
/// <code>
/// [IgnoreTargetFactory(typeof(Customer), Reason = "EF Core entity; setters are the persistence write path")]
/// public static partial Customer ToEntity(CustomerDto source);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class IgnoreTargetFactoryAttribute(Type targetType) : Attribute
{
    /// <summary>The target type whose static factory this mapping may bypass.</summary>
    public Type TargetType { get; } = targetType;

    /// <summary>An optional reviewable explanation.</summary>
    public string? Reason { get; set; }
}
