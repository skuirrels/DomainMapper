using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Enables per-invocation source-reference preservation for a mapping root.
/// Reference identity is used; no domain or persistence key is inferred.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapReferenceTrackingAttribute : Attribute;
