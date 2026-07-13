using System.Diagnostics;

namespace DomainMap.Abstractions;

/// <summary>
/// Marks a mapper method as an invariant-preserving domain factory.
/// Source members are matched and mapped to the method parameters by name.
/// The method remains user-owned and can delegate to a domain constructor or named factory.
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
public sealed class DomainFactoryAttribute : Attribute;
