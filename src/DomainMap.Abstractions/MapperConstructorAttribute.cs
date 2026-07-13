using System.Diagnostics;

namespace DomainMap.Abstractions;

/// <summary>
/// Marks the constructor to be used when type gets activated by DomainMap.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor)]
[Conditional("DOMAINMAP_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapperConstructorAttribute : Attribute;
