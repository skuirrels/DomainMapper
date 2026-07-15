# DomainMap

**Map data. Preserve intent.**

DomainMap is a compile-time object mapper for .NET 10 with a domain-driven design bias. It generates readable C# with no runtime reflection, while keeping constructors, named factories, value objects, and aggregate invariants under domain ownership.

> **Status:** v1.0.0 is available as a GitHub source release; NuGet publishing is intentionally deferred. See [NOTICE](NOTICE) for third-party attribution.

## The API

```csharp
using DomainMap.Abstractions;

[DomainMapper]
public static partial class OrdersMap
{
    [MapToFactory(nameof(Order.Place))]
    public static partial Order ToDomain(this PlaceOrder command);

    public static partial OrderDto ToDto(this Order order);
}
```

The generated construction path is intentionally boring and inspectable:

```csharp
var target = Order.Place(
    OrderId.Create(source.Id),
    CustomerName.Create(source.CustomerName),
    Money.Create(source.Total));
return target;
```

`[MapToFactory]` names the target-owned aggregate factory directly. DomainMap binds command members to its parameters and applies normal conversions, including conventional one-argument value-object factories such as `OrderId.Create(Guid)`. The factory is a required boundary: if DomainMap cannot satisfy it, compilation fails instead of silently choosing a public constructor or assigning properties.

For outbound DTO mapping, value objects can expose safe implicit conversions or conventional `ToX` methods. Use `[DomainFactory]` for advanced mapper-owned boundaries such as immutable updates, whole-source factories, or application result contracts.

## Design boundary

DomainMap owns data movement. Your domain owns behavior.

- Use generated mappings for commands, integration contracts, persistence models, read models, and projections.
- Use `[MapToFactory(nameof(Aggregate.Create))]` as the concise default for entering an aggregate through a target-owned static factory.
- Let conventional `Create(TValue)` methods construct strongly typed IDs and value objects.
- Use `[DomainFactory]` when the boundary is mapper-owned or needs the whole source value.
- Pass the current aggregate as an additional mapping parameter when an immutable update delegates to domain behavior. DomainMap will not guess that `Status = Shipped` means `order.Ship()`.
- Keep expected business failures explicit in your own result type; DomainMap does not introduce a runtime result abstraction.
- Project persistence entities to read models. Domain factories are deliberately rejected inside `IQueryable` projections.

## Capability surface

The inherited engine covers:

- new-instance and existing-target mappings;
- constructors, records, required/init members, and object factories;
- nested and flattened members;
- arrays, collections, dictionaries, spans, memory, tuples, and stacks;
- enums, strings, parsing, casts, and custom conversions;
- nullable annotations and configurable mismatch behavior;
- generics, inheritance, derived-type dispatch, and external mappers;
- reference preservation, deep cloning, and private-member access;
- `IQueryable` projection generation;
- incremental generation and analyzer diagnostics.

The suite combines broad structural mapping coverage with DomainMap-specific generator and runtime tests for required factory binding, strongly typed IDs, immutable aggregate updates, explicit failure results, projection rejection, and invariant failures.

## Build and test

```bash
dotnet restore DomainMap.slnx -p:HUSKY=0
dotnet build DomainMap.slnx -p:HUSKY=0 --no-restore
DiffEngine_Disabled=true dotnet test DomainMap.slnx -m:1 -p:HUSKY=0 --no-build --no-restore
```

## Benchmarks

The benchmark project pins Mapperly 4.3.1 stable and compiles both generators into the same .NET 10 process.

```bash
dotnet run -c Release \
  --project benchmarks/DomainMap.Benchmarks/DomainMap.Benchmarks.csproj \
  -- --filter '*ComparisonMappingBenchmarks*'

dotnet run -c Release \
  --project benchmarks/DomainMap.Benchmarks/DomainMap.Benchmarks.csproj \
  -- --filter '*SourceGeneratorBenchmarks*'
```

See [benchmark methodology and initial results](docs/benchmarks.md).

## Project layout

- `src/DomainMap.Abstractions` — the public attribute and configuration API.
- `src/DomainMap` — the incremental source generator and diagnostics.
- `test/DomainMap.Tests` — compile-time generation and diagnostics tests.
- `test/DomainMap.IntegrationTests` — generated-code runtime tests.
- `benchmarks/DomainMap.Benchmarks` — DomainMap versus Mapperly comparisons.

## License

DomainMap is licensed under Apache-2.0. Third-party attribution is preserved in [NOTICE](NOTICE).
