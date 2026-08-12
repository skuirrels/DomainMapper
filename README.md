# DomainMapper

**Map data. Preserve invariants.**

DomainMapper is a small compile-time mapper for .NET with a domain-driven design bias. Its source generator emits direct C# and does not use runtime reflection. The current package is `0.0.1-dev` and intentionally has a narrow, breaking API while the independent engine matures.

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

`[MapToFactory]` makes the target-owned factory mandatory. DomainMapper binds source properties to factory parameters by name and applies mapper-owned `[DomainFactory]` conversions where their source and target types match. If the target cannot be constructed, generation fails instead of bypassing the domain boundary.

## Current contract

The rewritten engine supports:

- mutable targets with accessible parameterless constructors;
- immutable targets and records with accessible constructors;
- target-owned static factories through `[MapToFactory]`;
- mapper-owned single-value conversions through `[DomainFactory]`;
- nested objects, arrays, lists, read-only collection targets, and dictionaries;
- existing-target property updates;
- direct generated code with optimized indexed loops for indexable collections.

Configuration-heavy mapping, private-member mutation, projections, reference preservation, derived-type dispatch, and other legacy compatibility features are intentionally not part of the new contract.

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

- `src/DomainMapper.Abstractions` — four public, DDD-facing API types.
- `src/DomainMapper/Engine` — contract discovery, semantic planning, conversion policy, and C# emission.
- `test/DomainMapper.Tests` — engine contract and performance-gate tests.
- `benchmarks/DomainMapper.Benchmarks` — balanced DomainMapper-versus-Mapperly evidence.
- `samples/DomainMapper.Sample` — aggregate construction through a domain factory.

## License

DomainMapper is licensed under Apache-2.0. The repository history includes an earlier Mapperly-derived implementation; attribution is retained in [NOTICE](NOTICE).
