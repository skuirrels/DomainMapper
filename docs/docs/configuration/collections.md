# Collections

DomainMapper maps arrays, lists, enumerable and read-only collection targets, and mutable or read-only dictionary interfaces element by element. General `IEnumerable<T>` sources are enumerated without assuming a `Count` property or indexer. Generated code preallocates capacity only when the source exposes a count, uses implemented collection interfaces when those capabilities are explicit, and backs interface targets with concrete `List<T>` or `Dictionary<TKey, TValue>` instances.

Nested element types use the same constructor and property conventions as root mappings.
