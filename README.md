# DomainMapper

[![NuGet](https://img.shields.io/nuget/v/DomainMapper.svg)](https://www.nuget.org/packages/DomainMapper)
[![NuGet downloads](https://img.shields.io/nuget/dt/DomainMapper.svg)](https://www.nuget.org/packages/DomainMapper)

**Map data. Preserve invariants.**

DomainMapper is a small compile-time mapper for .NET with a domain-driven design bias. Its source generator emits direct C# and does not use runtime reflection. Version `1.2` adds opt-in collection policies, reference preservation, runtime dispatch, and query projections while retaining domain-owned constructors and factories.

## Version 1.2.2

Install DomainMapper from NuGet with:

```bash
dotnet add package DomainMapper --version 1.2.2
```

## Domain-first mapping

```csharp
using DomainMapper.Abstractions;

[DomainMapper]
public static partial class OrderMapper
{
    [DomainFactory(Input = DomainFactoryInput.Source)]
    private static OrderId ToOrderId(int value) => new(value);

    [MapToFactory(nameof(Order.Place))]
    public static partial Order Place(OrderDraft source);
}
```

`[MapToFactory]` makes the target-owned factory mandatory. DomainMapper binds source properties and additional mapping parameters to factory parameters by name and applies mapper-owned `[DomainFactory]` conversions where their source and target types match. Explicit mapping parameters take precedence over same-named root source properties. If the target cannot be constructed completely, generation fails instead of bypassing the domain boundary or silently dropping state.

## Supported mappings

DomainMapper 1.2 supports:

- mutable targets with accessible parameterless constructors;
- immutable targets and records with accessible constructors;
- target-owned static factories through `[MapToFactory]`;
- mapper-owned source-value and member-bound conversions through `[DomainFactory]`;
- nested objects, arrays, lists, read-only collection targets, and mutable or read-only dictionary interfaces;
- existing-target property updates;
- explicit property and field renames, including nested source paths;
- typed computed target members and additional mapping parameters bound to factory, constructor, and settable members;
- target/source completeness, typed ignores, and allow-listed partial updates;
- conditional and null-aware assignments with constant substitution;
- typed completion hooks, mapping composition, and bounded recursion;
- `Replace`, `ClearAndFill`, and `Append` policies for existing-target collections;
- invocation-local reference preservation for mutable cyclic graphs;
- closed-world generated runtime dispatch with explicit derived-source opt-in;
- cached provider-neutral expression projections through the separate `DomainMapper.Projections` contract package;
- nested and generic mapper types and generic mapping methods;
- reuse of declared single-parameter mapping methods for nested values, so child entities honour their own factories and contracts;
- direct generated code that enumerates general sequences and preallocates only when the source exposes a count.

Fields participate when they are named by an explicit mapping contract; convention mapping remains property-only for compatibility. See the [authoritative capabilities and limitations](docs/docs/configuration/capabilities.md) and [versioned changelog](CHANGELOG.md).

Construction is fail-closed: every accessible writable target member must be mapped, and source-matched target state that is not writable from the generated mapper is rejected with `DMPR101`. Constructing a target that declares a static factory without going through it reports warning `DMPR108`.

Unsupported projection or tracking shapes fail at build time. Private-member mutation remains unsupported, and no feature scans assemblies, infers persistence semantics, or introduces mutable runtime mapping configuration.

## Build and test

```bash
dotnet restore DomainMapper.slnx -p:HUSKY=0
dotnet build DomainMapper.slnx --configuration Release --no-restore -p:HUSKY=0
dotnet test test/DomainMapper.Tests/DomainMapper.Tests.csproj --configuration Release --no-build -p:HUSKY=0
```

Run the sample with:

```bash
dotnet run --project samples/DomainMapper.Sample/DomainMapper.Sample.csproj
```

## Benchmarks

The comparison suite pins Mapperly 4.3.1 and measures both generated runtime mappings and cold source generation in balanced execution orders.

```bash
DOMAINMAPPER_BENCHMARK_PAIRS=6 ./scripts/run-stable-benchmarks.sh
```

See [the benchmark methodology](docs/benchmarks.md).

## Project layout

- `src/DomainMapper.Abstractions` — public, compile-time mapping attributes and policy types.
- `src/DomainMapper.Projections` — optional provider-neutral projection declaration contract.
- `src/DomainMapper/Engine` — contract discovery, semantic planning, conversion policy, and C# emission.
- `test/DomainMapper.Tests` — engine contract and performance-gate tests.
- `benchmarks/DomainMapper.Benchmarks` — balanced DomainMapper-versus-Mapperly evidence.
- `samples/DomainMapper.Sample` — aggregate construction through a domain factory.

## License

DomainMapper is licensed under Apache-2.0. Third-party attribution is retained in [NOTICE](NOTICE).
