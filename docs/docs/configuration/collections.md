# Collections

DomainMapper maps arrays, lists, read-only collection targets, and dictionaries element by element. It preallocates capacity when the source exposes a count and uses an indexed loop for indexable sources. This keeps generated runtime paths allocation-equivalent to hand-written loops.

Nested element types use the same constructor and property conventions as root mappings.
