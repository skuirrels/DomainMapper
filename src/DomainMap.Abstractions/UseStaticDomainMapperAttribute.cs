using System.Diagnostics;

namespace DomainMap.Abstractions;

/// <summary>
/// Considers all static mapping methods provided by the type.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
[Conditional("DOMAINMAP_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class UseStaticDomainMapperAttribute : Attribute
{
    /// <summary>
    /// Considers all static mapping methods provided by the <paramref name="mapperType"/>.
    /// </summary>
    /// <param name="mapperType">The type of which mapping methods will be included.</param>
    public UseStaticDomainMapperAttribute(Type mapperType) { }
}

/// <summary>
/// Considers all static mapping methods provided by the generic type.
/// </summary>
/// <typeparam name="T">The type of which mapping methods will be included.</typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
[Conditional("DOMAINMAP_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class UseStaticDomainMapperAttribute<T> : Attribute;
