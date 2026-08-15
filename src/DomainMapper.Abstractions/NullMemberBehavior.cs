namespace DomainMapper.Abstractions;

/// <summary>
/// Defines generated behavior when a configured nullable source member is null.
/// </summary>
public enum NullMemberBehavior
{
    /// <summary>Assign null to a nullable target member.</summary>
    Assign,

    /// <summary>Preserve the current target value. Valid only for existing-target mappings.</summary>
    PreserveTarget,

    /// <summary>Throw <see cref="InvalidOperationException"/>.</summary>
    Throw,

    /// <summary>Map a null collection to an empty collection.</summary>
    EmptyCollection,
}
