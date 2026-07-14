# Benchmarks

DomainMap compares itself against Mapperly 4.3.1, the latest stable release when this harness was added. The version is pinned so results remain reproducible.

## What is measured

`ComparisonMappingBenchmarks` runs paired generated mappings over identical types:

- flat objects;
- nested objects with collections;
- updates to an existing target;
- aggregate creation through an invariant-preserving factory;
- strongly typed value construction through a whole-source domain factory.

Existing-target benchmarks return the mutated target to BenchmarkDotNet. This makes the writes observable and prevents the JIT from reducing a mapping to dead stores when both mappers update preallocated objects.

`SourceGeneratorBenchmarks` runs each incremental generator over the same in-memory Roslyn compilation. BenchmarkDotNet launches the DomainMap and Mapperly cases in separate processes so tiered JIT and GC state cannot leak from one implementation into the other. Starting from immutable generator drivers makes each invocation a comparable cold generation pass without MSBuild, filesystem, or named-pipe noise.

## Current verification result

Captured on 14 July 2026 using .NET SDK 10.0.300 and .NET 10.0.8 on an Arm64 Apple M4 Pro. These are local observations, not a general performance claim.

| Runtime scenario     | DomainMap |  Mapperly | DomainMap ratio |    Allocation |
| -------------------- | --------: | --------: | --------------: | ------------: |
| Domain factory       |  4.345 ns |  4.621 ns |            0.94 |   64 B / 64 B |
| Existing target      |  0.746 ns |  0.731 ns |            1.02 |     0 B / 0 B |
| Flat object          |  4.205 ns |  3.878 ns |            1.08 |   64 B / 64 B |
| Nested collection    | 29.904 ns | 29.338 ns |            1.02 | 288 B / 288 B |
| Value-object factory |  2.045 ns |  2.028 ns |            1.01 |   24 B / 24 B |

The generated runtime code has the same shape in the non-factory scenarios, and the DDD-facing API does not introduce a runtime abstraction layer. Small sub-nanosecond differences should not be treated as meaningful without repeated runs on dedicated hardware.

The isolated cold source-generation run measured DomainMap at 2.037 ms and 1,657,730 B versus Mapperly at 1.712 ms and 1,510,314 B: `1.19x` time and `1.10x` allocation. An immediately preceding isolated run measured a `1.16x` time ratio and the same `1.10x` allocation ratio. This is within the parity gate but remains an optimization target.

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
- paired `ComparisonMappingBenchmarks` fail when DomainMap exceeds Mapperly by more than `1.25x` mean time plus a 1 ns absolute timing allowance, or `1.10x` allocated bytes plus a 64-byte noise allowance.
- paired `SourceGeneratorBenchmarks` fail when DomainMap exceeds Mapperly by more than `1.25x` mean time or `1.20x` allocated bytes.

The paired gate writes `DomainMap-vs-Mapperly-gate.md` and `DomainMap-vs-Mapperly-gate.json` beside the BenchmarkDotNet output. Raw and derived benchmark artifacts are retained by GitHub Actions for 90 days. Main-branch baselines use immutable, run-specific cache keys with a prefix restore, so no personal access token is required to replace a cache.

The timing allowance prevents sub-nanosecond jitter from becoming a large percentage regression for very small mappings; the relative threshold remains the primary guard as workloads grow. These thresholds are regression tripwires, not proof of general performance superiority. GitHub-hosted runners are shared hardware; investigate a failure with repeated runs on stable hardware before changing the limit.
