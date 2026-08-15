using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>Opts one registry mapping into assignable derived-source dispatch.</summary>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapRegistryDerivedAttribute : Attribute;
