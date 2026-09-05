# Architecture

The generator has four boundaries:

1. `DomainMapperGenerator` discovers attributed mapper declarations through Roslyn's incremental API and runs the compiler inside the transform, caching only emitted source and diagnostic data so no compilation or symbol outlives a run.
2. `MapperCompiler` turns partial methods into fail-closed mapping contracts and construction plans.
3. Conversion policy selects direct, factory, object, sequence, or dictionary conversion.
4. `SourceWriter` emits deterministic C# without runtime dependencies.

Generated declarations preserve the mapper's containing-type hierarchy and generic constraints, while fully qualified symbol identities and unique hint names prevent collisions between namespaces and overload shapes. Nested helpers carry generic method context and additional factory values explicitly. A failed contract does not suppress valid sibling contracts.

Generated hot paths are protected by code-fingerprint and BenchmarkDotNet gates. New capabilities should extend the semantic plan rather than introduce feature-specific descriptor hierarchies.
