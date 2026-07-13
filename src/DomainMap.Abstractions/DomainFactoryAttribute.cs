using System.Diagnostics;

namespace DomainMap.Abstractions;

/// <summary>
/// Marks a mapper method as a required invariant-preserving domain factory.
/// The mapping input is supplied according to <see cref="Input"/>.
/// The method remains user-owned and can delegate to a domain constructor or named factory.
/// DomainMap reports an error instead of falling back to ordinary construction when the factory cannot be used.
/// </summary>
/// <example>
/// <code>
/// [DomainFactory]
/// private static Order CreateOrder(OrderId id, CustomerId customerId)
///     => Order.Create(id, customerId);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
[Conditional("DOMAINMAP_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class DomainFactoryAttribute : Attribute
{
    /// <summary>
    /// Defines how the mapping source is supplied to the factory.
    /// DomainMap reports an error instead of falling back to ordinary construction when the factory cannot be used.
    /// </summary>
    public DomainFactoryInput Input { get; set; } = DomainFactoryInput.Members;
}
