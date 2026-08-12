using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Configures a mapping method to construct its target through a named static factory on the target type.
/// Source members are mapped to factory parameters by name and normal mapping conversions are applied.
/// DomainMapper reports an error instead of falling back to a constructor or member assignment when the factory cannot be used.
/// </summary>
/// <example>
/// <code>
/// [MapToFactory(nameof(Order.Create))]
/// public static partial Order ToDomain(CreateOrderDto source);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapToFactoryAttribute(string factoryMethodName) : Attribute
{
    /// <summary>
    /// The name of the static factory method on the mapping target type.
    /// </summary>
    public string FactoryMethodName { get; } = factoryMethodName;
}
