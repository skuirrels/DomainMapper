using System.Diagnostics;

namespace DomainMap.Abstractions;

/// <summary>
/// Considers all accessible mapping methods provided by the type of this member.
/// Includes static and instance methods.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
[Conditional("DOMAINMAP_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class UseDomainMapperAttribute : Attribute;
