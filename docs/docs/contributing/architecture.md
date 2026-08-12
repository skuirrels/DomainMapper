# Architecture

The generator has four boundaries:

1. `DomainMapperGenerator` discovers attributed mapper declarations through Roslyn's incremental API.
2. `MapperCompiler` turns partial methods into mapping contracts and construction plans.
3. Conversion policy selects direct, factory, object, sequence, or dictionary conversion.
4. `SourceWriter` emits deterministic C# without runtime dependencies.

Generated hot paths are protected by code-fingerprint and BenchmarkDotNet gates. New capabilities should extend the semantic plan rather than introduce feature-specific descriptor hierarchies.
