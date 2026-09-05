# Changelog

All notable changes to DomainMapper are recorded here. The project follows semantic versioning.

## [Unreleased]

### Added

- Enum pairs map by member name through a generated switch with a throwing default. Unmatched, aliased, or `[Flags]` members fail with `DMPR109`; a `[DomainFactory]` for the pair takes precedence.
- Additional mapping parameters bind to constructor parameters and settable members, not only target factories. Root parameters take precedence over same-named source members; nested helpers use them to fill members the nested source lacks.
- Warning `DMPR108` reports convention construction of a target that declares an accessible static factory method, so bypassed domain factories are visible in review and can be promoted to errors with `WarningsAsErrors`. `[IgnoreTargetFactory]` records an accepted bypass per mapping and type and is validated as stale when unused.
- Nested values are mapped through a declared static single-parameter mapping method for the same source and target pair, so child entities honour `[MapToFactory]` and explicit contracts. Ambiguous or cyclic reuse is rejected with `DMPR102`.

### Fixed

- `Nullable<T>` no longer exposes `Value` and `HasValue` as convention source members, so a nullable source can no longer be unwrapped into a non-nullable target without a null policy.
- Nullable value types lift through domain factories, single-value constructors, and convention helpers on either side, so `int?` maps to a nullable strongly typed identifier and `string?` maps to a nullable value-type wrapper.
- Parameterless construction that consumes no source data now fails with `DMPR101` instead of emitting `new T()` and silently dropping the source value behind default state.

## [1.2.2] - 2026-09-05

### Changed

- Generated members are stamped with the generator assembly version instead of a fixed `0.0.1.0`.
- Mapping attributes are matched by symbol instead of display-name strings.
- Deferred target construction is planned structurally instead of through an encoded text marker.
- The compiler engine is split into partial-class files by responsibility.
- Release and CI versions derive from `Directory.Build.props` instead of hardcoded strings.
- Generated diagnostics use file-based locations rehydrated from cached data.

### Fixed

- The incremental pipeline caches emitted source and diagnostic data instead of symbols, so cached mappers no longer keep old compilations alive and every contract edit is observed.
- Package validation compares against the latest published release.

## [1.2.1] - 2026-08-20

### Changed

- Updated Meziantou.Analyzer, Meziantou.Polyfill, the NuGet package validation tool, and grouped GitHub Actions dependencies.

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

[Unreleased]: https://github.com/skuirrels/DomainMapper/compare/v1.2.2...HEAD
[1.2.2]: https://github.com/skuirrels/DomainMapper/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/skuirrels/DomainMapper/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/skuirrels/DomainMapper/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/skuirrels/DomainMapper/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/skuirrels/DomainMapper/releases/tag/v1.0.0
