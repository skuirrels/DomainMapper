using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Generates closed-world runtime dispatch for eligible static mapping methods declared by this mapper.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapRegistryAttribute : Attribute;
