# Collections

DomainMapper maps arrays, lists, enumerable and read-only collection targets, and mutable or read-only dictionary interfaces element by element. General `IEnumerable<T>` sources are enumerated without assuming a `Count` property or indexer. Generated code preallocates capacity only when the source exposes a count, uses implemented collection interfaces when those capabilities are explicit, and backs interface targets with concrete `List<T>` or `Dictionary<TKey, TValue>` instances.

Nested element types use the same constructor and property conventions as root mappings.

## Existing-target policies

```csharp
[MapOnlyTargetMembers(nameof(Target.Items))]
[MapCollection(nameof(Target.Items), CollectionUpdatePolicy.ClearAndFill)]
public static partial void Apply(Source source, Target target);
```

- `Replace` maps a new collection and assigns it to a writable member.
- `ClearAndFill` clears an existing mutable collection, then adds mapped source elements in source order.
- `Append` keeps existing elements and adds mapped source elements in source order.

Sequence duplicates are preserved. Dictionary mutation calls `Add`, so a duplicate key throws rather than overwriting silently. A null source clears a clear/fill target and is a no-op for append unless `MapNull` selects `PreserveTarget` or `Throw`. The target collection itself must be non-null at runtime.

These policies are mechanical. DomainMapper does not match elements by key, infer entity identity, remove orphans, or interpret relationship ownership.
