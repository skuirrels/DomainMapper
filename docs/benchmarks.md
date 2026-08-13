# Benchmarks

DomainMapper compares generated runtime mappings and cold source generation with Mapperly 4.3.1. The comparison version is pinned for reproducibility.

## What is measured

`ComparisonMappingBenchmarks` exercises five paired mappings over identical types:

- flat mutable objects;
- nested records containing a concrete list;
- updates to an existing target;
- aggregate construction through a static domain factory;
- strongly typed value construction through a mapper-owned domain factory.

`SourceGeneratorBenchmarks` runs each incremental generator over the same in-memory Roslyn compilation. The fixture covers mutable and immutable objects, nullable properties, nested records, arrays, lists, read-only collections, dictionaries, generics, and existing-target updates. Setup verifies that generated output compiles before measurement.

BenchmarkDotNet runs each implementation in a separate process and alternates execution order. The gate aggregates raw iteration samples by median, checks a one-sided confidence bound, and permits no additional managed allocation.

## Rewrite verification — 12 August 2026

These local measurements used .NET SDK 10.0.400 and .NET 10.0.11 on an Arm64 Apple M4 Pro. They are migration evidence, not a general performance claim.

The runtime development gate used one balanced pair: two reports and six raw samples per implementation and scenario. Four paths had matching normalized generated-code fingerprints. The only differentiated path retained DomainMapper's indexed-list loop.

| Scenario             | Mapperly median | DomainMapper median | Mapperly allocation | DomainMapper allocation | Gate                                  | Winner               |
| -------------------- | --------------: | ------------------: | ------------------: | ----------------------: | ------------------------------------- | -------------------- |
| Domain factory       |        4.524 ns |            4.415 ns |                64 B |                    64 B | Proven code parity                    | No meaningful winner |
| Existing target      |        0.762 ns |            0.708 ns |                 0 B |                     0 B | Proven code parity                    | No meaningful winner |
| Flat object          |        3.895 ns |            3.911 ns |                64 B |                    64 B | Proven code parity                    | No meaningful winner |
| Nested collection    |       28.888 ns |           28.167 ns |               288 B |                   288 B | Faster with negative confidence bound | DomainMapper         |
| Value-object factory |        2.051 ns |            2.136 ns |                24 B |                    24 B | Proven code parity                    | No meaningful winner |

The development runtime gate passed. Allocation was equal in every scenario and execution order.

The full default local run then used six balanced pairs: 12 reports and 36 raw samples per implementation and scenario. Every scenario passed the unchanged time-regression limit and allocated exactly the same number of managed bytes as Mapperly.

| Scenario             | Mapperly median | DomainMapper median | Time ratio | Mapperly allocation | DomainMapper allocation | No-regression result | Winner               |
| -------------------- | --------------: | ------------------: | ---------: | ------------------: | ----------------------: | -------------------- | -------------------- |
| Domain factory       |       20.376 ns |            4.521 ns |     0.222x |                64 B |                    64 B | Pass                 | No meaningful winner |
| Existing target      |        0.775 ns |            0.772 ns |     0.995x |                 0 B |                     0 B | Pass                 | No meaningful winner |
| Flat object          |        4.034 ns |            4.006 ns |     0.993x |                64 B |                    64 B | Pass                 | No meaningful winner |
| Nested collection    |       29.703 ns |           29.096 ns |     0.980x |               288 B |                   288 B | Pass                 | No proven winner     |
| Value-object factory |        2.130 ns |            2.132 ns |     1.001x |                24 B |                    24 B | Pass                 | No meaningful winner |

The stronger differentiated-path claim did not pass in this full local run. Nested collection had a lower DomainMapper median, but severe host scheduling disturbances widened its one-sided upper difference bound to `3.029 ns`, so this evidence does not prove that path faster. The same path passed the balanced development gate with a `-0.554 ns` upper bound. The high-priority scheduling request was denied by the host, so repeat the strict gate on dedicated, idle hardware before making a release-level faster-than-Mapperly claim.

The finalized cold-generation gate used four balanced reports and exceeded its 40-sample minimum for each implementation:

| Measurement            |    Mapperly | DomainMapper | Gate                                  | Winner       |
| ---------------------- | ----------: | -----------: | ------------------------------------- | ------------ |
| Median cold generation |    1.766 ms |    76.411 μs | Faster with negative confidence bound | DomainMapper |
| Managed allocation     | 1,396,260 B |    198,548 B | No additional allocation              | DomainMapper |
| Reports / raw samples  |     4 / 218 |       4 / 71 | Minimum 4 reports and 40 samples      | DomainMapper |

The cold-generation gate passed at a 0.043 time ratio and approximately 14.2% of Mapperly's managed allocation in this run.

## Reproduce

```bash
./scripts/run-stable-benchmarks.sh
```

By default, the stable harness runs six balanced runtime pairs and two balanced cold-generation pairs. It writes environment metadata, generated-code parity fingerprints, raw BenchmarkDotNet reports, and Markdown/JSON gate results beneath a timestamped directory in `/tmp`.

Useful development overrides are:

```bash
DOMAINMAPPER_BENCHMARK_PAIRS=1 \
DOMAINMAPPER_SOURCE_BENCHMARK_PAIRS=2 \
DOMAINMAPPER_BENCHMARK_JOB=Short \
./scripts/run-stable-benchmarks.sh
```

Use the default six runtime pairs on dedicated, idle hardware before making a release-level performance claim.

## Regression policy

- Fingerprint-equivalent generated paths are classified as proven parity; sub-nanosecond jitter does not select a winner.
- Differentiated runtime paths must have a lower DomainMapper median and a negative one-sided 95% upper confidence bound.
- DomainMapper may not allocate more managed memory in any comparison run.
- The stable runtime decision requires 12 reports and 36 raw samples per implementation and scenario.
- The cold-generation decision requires four balanced reports, at least 40 raw samples per implementation, a lower median, a negative confidence bound, and no additional allocation.

The gate is a regression tripwire, not proof that one mapper is universally faster. Investigate failures on dedicated hardware before changing the policy.
