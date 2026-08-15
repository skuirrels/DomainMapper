# Changelog

All notable changes to DomainMapper are recorded here. The project follows semantic versioning.

## [Unreleased]

## [1.2.0] - 2026-08-15

### Added

- Explicit `Replace`, `ClearAndFill`, and `Append` existing-target collection policies.
- Invocation-local reference preservation for shared and cyclic mutable object graphs.
- Closed-world runtime dispatch through generated `TryMapRuntime` and `MapRuntime` methods.
- Cached provider-neutral expression projections through the optional `DomainMapper.Projections` package.
- Diagnostics `DMPR105` through `DMPR107` for unsupported reference tracking, projection eligibility, and registry declarations.
- Incremental invalidation, concurrency, trimming, and native AOT validation fixtures.

### Changed

- Incremental generator inputs are fingerprinted per mapper and reachable source contract so isolated changes keep unrelated mapper outputs cached.
- The 1.2 packages validate public API compatibility against the 1.1 release.

### Fixed

- Reference tracking distinguishes target contracts when one source instance participates in heterogeneous target shapes.
- Projection generation rejects failed mappings, custom delegates, and user-defined conversion calls while retaining pure lifted conversions.
- Runtime registries reject open-world interface ambiguity and value-type derived dispatch, handle nullable annotations, and exclude mappings whose deferred helpers fail.
- Incremental invalidation now includes containing partial types and inherited mapper declarations that affect emitted source.

## [1.1.0] - 2026-08-15

### Added

- Explicit property and field bindings, including validated nested source paths.
- Mapper-owned computed target members with typed source and additional-parameter inputs.
- Per-method target/source ignores and `Target`, `Source`, `Both`, or explicit `None` completeness policies.
- Allow-listed existing-target updates, conditional assignments, and per-member null behavior including constant substitution.
- Typed, ordered completion hooks.
- Compile-time reuse and derived overrides of explicit bindings through `IncludeMapping`.
- Opt-in bounded recursive mapping with generated depth guards; recursive contracts are rejected by default.
- Diagnostics `DMPR102` through `DMPR104` for stale configuration, source completeness, and the completeness escape hatch.

### Changed

- Accessible fields can now participate through explicit bindings while property-only convention mapping remains compatible with 1.0.
- Mapping methods may accept additional typed parameters for computed members, not only target-owned factories.

## [1.0.0] - 2026-08-13

### Added

- First public DomainMapper release.
- Compile-time convention mapping for mutable and immutable targets.
- Target-owned and mapper-owned factories, collection and dictionary mapping, and existing-target updates.

[Unreleased]: https://github.com/skuirrels/DomainMapper/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/skuirrels/DomainMapper/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/skuirrels/DomainMapper/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/skuirrels/DomainMapper/releases/tag/v1.0.0
