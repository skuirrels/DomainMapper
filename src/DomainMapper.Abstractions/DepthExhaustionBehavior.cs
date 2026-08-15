namespace DomainMapper.Abstractions;

/// <summary>Defines generated behavior when a recursive mapping reaches its configured maximum depth.</summary>
public enum DepthExhaustionBehavior
{
    /// <summary>Return the target type's default value.</summary>
    ReturnDefault,

    /// <summary>Throw <see cref="InvalidOperationException"/>.</summary>
    Throw,
}
