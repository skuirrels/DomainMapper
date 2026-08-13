namespace DomainMapper.Abstractions;

/// <summary>
/// Defines how a <see cref="DomainFactoryAttribute"/> receives data from a mapping.
/// </summary>
public enum DomainFactoryInput
{
    /// <summary>
    /// Binds source members and additional mapping parameters to factory parameters by name.
    /// This mode is intended for aggregate construction and immutable state transitions.
    /// </summary>
    Members,

    /// <summary>
    /// Passes the complete source value to the single factory parameter.
    /// This mode is intended for strongly typed identifiers and value objects.
    /// </summary>
    Source,
}
