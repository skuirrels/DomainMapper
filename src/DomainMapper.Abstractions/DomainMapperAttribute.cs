using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Marks a partial type as a compile-time domain mapper.
/// </summary>
/// <remarks>
/// Mapping declarations are intentionally convention based. Domain construction rules belong in
/// constructors or factory methods, not in mapper configuration properties.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class DomainMapperAttribute : Attribute;
