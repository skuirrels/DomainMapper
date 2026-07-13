using System.Diagnostics;

namespace DomainMap.Abstractions;

/// <summary>
/// Used to set mapper default values in the assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
[Conditional("DOMAINMAP_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapperDefaultsAttribute : DomainMapperAttribute;
