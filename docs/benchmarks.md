# Benchmarks

DomainMap compares itself against Mapperly 4.3.1, the latest stable release when this harness was added. The version is pinned so results remain reproducible.

## What is measured

`ComparisonMappingBenchmarks` runs paired generated mappings over identical types:

- flat objects;
- nested objects with collections;
- updates to an existing target;
- aggregate creation through an invariant-preserving factory;
- strongly typed value construction through a whole-source domain factory.

`SourceGeneratorBenchmarks` runs each incremental generator over the same in-memory Roslyn compilation. Starting from immutable generator drivers makes each invocation a comparable cold generation pass without MSBuild, filesystem, or named-pipe noise.

## Initial verification result

Captured on 13 July 2026 using .NET SDK 10.0.300 and .NET 10.0.8 on Arm64 macOS. These are local observations, not a general performance claim.

| Scenario               | DomainMap | Mapperly | DomainMap ratio |        Allocation |
| ---------------------- | --------: | -------: | --------------: | ----------------: |
| Flat runtime mapping   |  4.142 ns | 4.512 ns |            0.92 |       64 B / 64 B |
| Cold source generation |  1.753 ms | 2.006 ms |            0.87 | 1.36 MB / 1.41 MB |

The correct interpretation is parity: generated runtime code has the same shape, and the DDD-facing API does not introduce a runtime abstraction layer. Small sub-nanosecond differences should not be treated as meaningful without repeated runs on dedicated hardware.

## Reproduce

```bash
dotnet run -c Release \
  --project benchmarks/DomainMap.Benchmarks/DomainMap.Benchmarks.csproj \
  -- --filter '*ComparisonMappingBenchmarks*'

dotnet run -c Release \
  --project benchmarks/DomainMap.Benchmarks/DomainMap.Benchmarks.csproj \
  -- --filter '*SourceGeneratorBenchmarks*'
```

BenchmarkDotNet writes CSV, Markdown, and HTML reports under `artifacts/results`.

## Regression gates

The benchmark workflow applies two complementary gates:

- historical results fail when mean execution time exceeds the retained main-branch baseline by more than `1.25x`;
- paired `ComparisonMappingBenchmarks` fail when DomainMap exceeds Mapperly by more than `1.25x` mean time, or `1.10x` allocated bytes plus a 64-byte noise allowance.

The paired gate writes `DomainMap-vs-Mapperly-gate.md` and `DomainMap-vs-Mapperly-gate.json` beside the BenchmarkDotNet output. Raw and derived benchmark artifacts are retained by GitHub Actions for 90 days. Main-branch baselines use immutable, run-specific cache keys with a prefix restore, so no personal access token is required to replace a cache.

These thresholds are regression tripwires, not proof of general performance superiority. GitHub-hosted runners are shared hardware; investigate a failure with repeated runs on stable hardware before changing the limit.
