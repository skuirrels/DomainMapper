using System.Diagnostics;

namespace DomainMap.Abstractions;

/// <summary>
/// Marks a given parameter as the mapping target.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
[Conditional("DOMAINMAP_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MappingTargetAttribute : Attribute;
