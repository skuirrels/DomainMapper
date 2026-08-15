namespace DomainMapper.Abstractions;

/// <summary>Defines how an existing target collection is updated.</summary>
public enum CollectionUpdatePolicy
{
    /// <summary>Replace the target member with a newly mapped collection.</summary>
    Replace,

    /// <summary>Clear the existing target collection and add mapped source elements in source order.</summary>
    ClearAndFill,

    /// <summary>Add mapped source elements to the existing target collection in source order.</summary>
    Append,
}
