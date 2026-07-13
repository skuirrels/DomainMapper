# DomainMap

**Map data. Preserve intent.**

DomainMap is a compile-time object mapper for .NET 10 with a domain-driven design bias. It generates readable C# with no runtime reflection, while keeping constructors, named factories, value objects, and aggregate invariants under domain ownership.

> Status: experimental standalone fork. DomainMap is derived from [Mapperly](https://github.com/riok/mapperly) under Apache-2.0 and retains its mature structural mapping engine. See [NOTICE](NOTICE).

## The API

```csharp
using DomainMap.Abstractions;

[DomainMapper]
public static partial class OrdersMap
{
    [DomainFactory]
    private static Order Create(
        OrderId id,
        CustomerName customerName,
        Money total)
        => Order.Place(id, customerName, total);

    private static OrderId ToOrderId(Guid value) => new(value);
    private static CustomerName ToCustomerName(string value) => CustomerName.Create(value);
    private static Money ToMoney(decimal value) => Money.Gbp(value);

    public static partial Order ToDomain(PlaceOrder command);
    public static partial OrderView ToView(Order order);
}
```

The generated construction path is intentionally boring and inspectable:

```csharp
var target = Create(
    ToOrderId(source.Id),
    ToCustomerName(source.CustomerName),
    ToMoney(source.Total));
return target;
```

`[DomainFactory]` is the DDD-focused difference: its parameters are bound from source members automatically. The factory body remains ordinary user code, so validation and invariants are never reimplemented by the generator.

## Design boundary

DomainMap owns data movement. Your domain owns behavior.

- Use generated mappings for commands, integration contracts, persistence models, read models, and projections.
- Use `[DomainFactory]` to enter an aggregate through a constructor or named factory.
- Use small user mappings for strongly typed IDs and value objects.
- Keep aggregate updates that require behavior as explicit domain method calls. DomainMap will not guess that `Status = Shipped` means `order.Ship()`.

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

The suite retains Mapperly's broad conformance corpus and adds DomainMap-specific generator and runtime tests for factory binding, optional arguments, strongly typed IDs, null guards, generic factories, fallback selection, inheritance, and invariant failures.

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

## License and origin

DomainMap is licensed under Apache-2.0. It is an independent derivative work and is not affiliated with or endorsed by the Mapperly maintainers. The original project and contributor attribution are preserved in [NOTICE](NOTICE).
