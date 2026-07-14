# Benchmarks

DomainMap compares itself against Mapperly 4.3.1, the latest stable release when this harness was added. The version is pinned so results remain reproducible.

## What is measured

`ComparisonMappingBenchmarks` runs paired generated mappings over identical types:

- flat objects;
- nested objects with collections;
- updates to an existing target;
- aggregate creation through an invariant-preserving factory;
- strongly typed value construction through a whole-source domain factory.

BenchmarkDotNet launches each comparison case in its own process. This isolates tiered JIT, GC, and benchmark ordering state, which is important when equivalent generated methods complete within a few nanoseconds of each other.

Existing-target benchmarks return the mutated target to BenchmarkDotNet. This makes the writes observable and prevents the JIT from reducing a mapping to dead stores when both mappers update preallocated objects.

`SourceGeneratorBenchmarks` runs each incremental generator over the same in-memory Roslyn compilation. BenchmarkDotNet launches the DomainMap and Mapperly cases in separate processes so tiered JIT and GC state cannot leak from one implementation into the other. Starting from immutable generator drivers makes each invocation a comparable cold generation pass without MSBuild, filesystem, or named-pipe noise.

## Current verification result

Captured on 14 July 2026 using .NET SDK 10.0.300 and .NET 10.0.8 on an Arm64 Apple M4 Pro. The result combines two complete `ShortRun` matrices with opposite Mapperly-first and DomainMap-first execution orders, giving six raw iteration samples per implementation and scenario. These are local observations, not a general performance claim.

| Runtime scenario     | Mapperly median | DomainMap median | Ratio | Upper difference bound |    Allocation | Gate classification |
| -------------------- | --------------: | ---------------: | ----: | ---------------------: | ------------: | ------------------- |
| Domain factory       |        4.386 ns |         4.757 ns | 1.085 |               0.844 ns |   64 B / 64 B | Proven code parity  |
| Existing target      |        0.793 ns |         0.740 ns | 0.934 |              -0.034 ns |     0 B / 0 B | Faster              |
| Flat object          |        4.129 ns |         3.775 ns | 0.914 |              -0.250 ns |   64 B / 64 B | Faster              |
| Nested collection    |       30.139 ns |        28.608 ns | 0.949 |              -0.920 ns | 288 B / 288 B | Faster              |
| Value-object factory |        2.117 ns |         2.084 ns | 0.984 |               0.032 ns |   24 B / 24 B | Proven code parity  |

The strict combined gate passed. Every differentiated generated path has both a lower median and a negative one-sided 95% upper confidence bound for DomainMap minus Mapperly. Allocation is equal in every scenario and in both execution orders.

The flat and existing-target improvements come from applying `AggressiveInlining` only to small, invocation-free leaf mappings. The nested-collection improvement comes from preserving a concrete `List<T>` source and generating an indexed loop with a local element variable. The local variable retains the previous null-element exception behavior while avoiding enumerator overhead.

The domain-factory and value-object-factory paths normalize to matching SHA-256 fingerprints after trivial local factory wrappers are inlined. Their raw sub-nanosecond timing differences are therefore treated as execution noise rather than as different mapping work. The parity fingerprint retains performance-affecting attributes, so the inlined leaf mappings are correctly classified as differentiated paths.

The cleaner `[MapToFactory(nameof(BenchmarkAggregate.Create))]` declaration continues to generate a direct factory call without a runtime wrapper or extra allocation.

The isolated cold source-generation run measured DomainMap at 2.110 ms and 1,701,692 B versus Mapperly at 1.927 ms and 1,538,005 B: `1.095x` time and `1.11x` allocation. This is within the `1.25x` time and `1.20x` generator-allocation gates but remains an optimization target.

## Reproduce

```bash
DOMAINMAP_BENCHMARK_ORDER=mapperly-first \
DOMAINMAP_BENCHMARK_ARTIFACTS=/tmp/domainmap-mapperly-first \
dotnet run -c Release \
  --project benchmarks/DomainMap.Benchmarks/DomainMap.Benchmarks.csproj \
  -- --exporters json --job Short --filter '*ComparisonMappingBenchmarks*'

DOMAINMAP_BENCHMARK_ORDER=domainmap-first \
DOMAINMAP_BENCHMARK_ARTIFACTS=/tmp/domainmap-domainmap-first \
dotnet run -c Release \
  --project benchmarks/DomainMap.Benchmarks/DomainMap.Benchmarks.csproj \
  -- --exporters json --job Short --filter '*ComparisonMappingBenchmarks*'
```

BenchmarkDotNet writes CSV, Markdown, and HTML reports under `artifacts/results`.

## Regression gates

The benchmark workflow applies two complementary gates:

- historical results fail when mean execution time exceeds the retained main-branch baseline by more than `1.25x`;
- paired `ComparisonMappingBenchmarks` run in both implementation orders and aggregate BenchmarkDotNet's raw iteration samples by median;
- scenarios with matching generated-code fingerprints are accepted as proven code parity, while every differentiated scenario must have a lower DomainMap median and a negative one-sided 95% upper confidence bound for DomainMap minus Mapperly;
- every runtime comparison run requires DomainMap allocated bytes to be equal to or lower than Mapperly, with no ratio or byte allowance;
- paired `SourceGeneratorBenchmarks` fail when DomainMap exceeds Mapperly by more than `1.25x` mean time or `1.20x` allocated bytes.

The paired gate writes `DomainMap-vs-Mapperly-gate.md` and `DomainMap-vs-Mapperly-gate.json` beside the BenchmarkDotNet output. Raw and derived benchmark artifacts are retained by GitHub Actions for 90 days. Main-branch baselines use immutable, run-specific cache keys with a prefix restore, so no personal access token is required to replace a cache.

Generated-code parity prevents sub-nanosecond jitter from deciding a winner when both tools emit the same effective work. Differentiated paths have no timing slack: they must demonstrate a statistical win. These gates are regression tripwires, not proof of general performance superiority. GitHub-hosted runners are shared hardware; investigate a failure with repeated balanced runs on stable hardware before changing the policy.
