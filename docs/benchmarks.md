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

Captured on 15 July 2026 using .NET SDK 10.0.300 and .NET 10.0.8 on an Arm64 Apple M4 Pro. The result combines six balanced pairs of complete `ShortRun` matrices, alternating which implementation runs first. This gives 12 reports and 36 raw iteration samples per implementation and scenario. These are local observations, not a general performance claim.

The 36-sample result combines two independent six-report campaigns. Between them, the harness added matching `MethodImpl` declarations to the three fingerprint-equivalent parity scenarios; the generated and compiled flat-object and nested-collection paths did not change. The parity timings below are included for completeness and are not used to claim a winner.

| Runtime scenario     | Mapperly median | DomainMap median | Ratio | Upper difference bound |    Allocation | Gate classification |
| -------------------- | --------------: | ---------------: | ----: | ---------------------: | ------------: | ------------------- |
| Domain factory       |        4.545 ns |         4.583 ns | 1.009 |               0.115 ns |   64 B / 64 B | Proven code parity  |
| Existing target      |        0.767 ns |         0.770 ns | 1.003 |               0.079 ns |     0 B / 0 B | Proven code parity  |
| Flat object          |        4.223 ns |         3.974 ns | 0.941 |              -0.243 ns |   64 B / 64 B | Faster              |
| Nested collection    |       30.363 ns |        29.222 ns | 0.962 |              -0.145 ns | 288 B / 288 B | Faster              |
| Value-object factory |        2.112 ns |         2.156 ns | 1.021 |               0.117 ns |   24 B / 24 B | Proven code parity  |

The strict combined gate passed. Every differentiated generated path has both a lower median and a negative one-sided 95% upper confidence bound for DomainMap minus Mapperly. Allocation is equal in every scenario and in both execution orders.

The flat-object improvement comes from applying `AggressiveInlining` only to a small, invocation-free root mapping where repeated measurements show a real benefit. The nested-collection improvement comes from preserving a concrete `List<T>` source and generating an indexed loop with a local element variable. The local variable retains the previous null-element exception behavior while avoiding enumerator overhead.

The domain-factory, existing-target, and value-object-factory paths normalize to matching SHA-256 fingerprints after trivial local factory wrappers are inlined. Their raw sub-nanosecond timing differences are therefore treated as execution noise rather than as different mapping work. The parity fingerprint retains performance-affecting attributes, preventing an inline hint from being hidden inside an equivalence classification.

The cleaner `[MapToFactory(nameof(BenchmarkAggregate.Create))]` declaration generates a direct factory call without a runtime mapper wrapper or extra allocation. Whole-source domain-factory expressions are also fused only when the source parameter is evaluated once, preserving getter and side-effect semantics.

The isolated cold source-generation run is now a statistical win:

| Generator |   Median |  Allocation | Result |
| --------- | -------: | ----------: | ------ |
| Mapperly  | 2.407 ms | 1,495,007 B | —      |
| DomainMap | 2.142 ms | 1,420,485 B | Faster |

The median is 11.0% lower, allocation is 5.0% lower, and the one-sided 95% upper bound for DomainMap minus Mapperly is -0.256 ms.

### Linux Arm64 validation

A virtualized Linux Arm64 run under Docker Desktop used two balanced pairs, producing four reports and 12 samples per implementation and runtime scenario. Flat-object and nested-collection medians were respectively 5.6% and 3.0% lower, with equal allocation. One complete report was contaminated by host scheduling noise, so the conservative confidence bounds remained positive and the runtime gate correctly classified the Linux result as inconclusive rather than faster.

The isolated Linux cold-generator gate passed: DomainMap measured 2.271 ms and 1,482,506 B versus 2.480 ms and 1,574,648 B, an 8.4% lower median and 5.9% lower allocation. Docker results are portability evidence, not a substitute for the dedicated-hardware workflow.

## Reproduce

```bash
./scripts/run-stable-benchmarks.sh
```

The script builds once, writes environment metadata, runs six runtime pairs in alternating orders, proves generated-code parity, applies the runtime gate, runs the cold-generator comparison in both implementation orders, and applies its statistical/allocation gate. Set `DOMAINMAP_STABLE_RESULT_ROOT` to choose the evidence directory.

The manual `benchmark-stable` GitHub workflow runs the same script on a self-hosted runner labelled `linux` and `benchmark`. That runner should be dedicated, idle, fixed-power hardware. A release performance claim should use its retained 12-report artifact, not a shared GitHub-hosted runner or virtualized laptop result.

## Regression gates

The benchmark workflow applies two complementary gates:

- historical results fail when mean execution time exceeds the retained main-branch baseline by more than `1.25x`;
- paired `ComparisonMappingBenchmarks` run in both implementation orders and aggregate BenchmarkDotNet's raw iteration samples by median;
- scenarios with matching generated-code fingerprints are accepted as proven code parity, while every differentiated scenario must have a lower DomainMap median and a negative one-sided 95% upper confidence bound for DomainMap minus Mapperly;
- every runtime comparison run requires DomainMap allocated bytes to be equal to or lower than Mapperly, with no ratio or byte allowance;
- shared GitHub-hosted `SourceGeneratorBenchmarks` run in both implementation orders as a regression guard, allowing at most `1.25x` median time and `1.10x` allocation because shared x64 runners cannot reliably prove a statistical winner;
- dedicated-runner `SourceGeneratorBenchmarks` require a lower median, a negative one-sided 95% upper confidence bound, and allocation no greater than the comparison implementation;
- the stable harness refuses a runtime decision with fewer than 12 reports or 36 raw samples per implementation and refuses a cold-generator decision with fewer than two reports or 40 samples.

The paired gate writes `DomainMap-vs-Mapperly-gate.md` and `DomainMap-vs-Mapperly-gate.json` beside the BenchmarkDotNet output. Raw and derived benchmark artifacts are retained by GitHub Actions for 90 days. Main-branch baselines use immutable, run-specific cache keys with a prefix restore, so no personal access token is required to replace a cache.

Generated-code parity prevents sub-nanosecond jitter from deciding a winner when both tools emit the same effective work. Differentiated paths have no timing slack: they must demonstrate a statistical win. These gates are regression tripwires, not proof of general performance superiority. GitHub-hosted runners are shared hardware; investigate a failure with the manual dedicated-runner workflow before changing the policy.
