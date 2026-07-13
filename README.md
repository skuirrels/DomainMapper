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
    [DomainFactory(Input = DomainFactoryInput.Source)]
    private static OrderId ToOrderId(Guid value) => OrderId.Create(value);

    [DomainFactory(Input = DomainFactoryInput.Source)]
    private static CustomerName ToCustomerName(string value)
        => CustomerName.Create(value);

    [DomainFactory(Input = DomainFactoryInput.Source)]
    private static Money ToMoney(decimal value) => Money.Gbp(value);

    [DomainFactory]
    private static Order Create(
        OrderId id,
        CustomerName customerName,
        Money total)
        => Order.Place(id, customerName, total);

    [DomainFactory]
    private static Order Rename(Order current, CustomerName customerName)
        => current.Rename(customerName);

    public static partial Order ToDomain(PlaceOrder command);
    public static partial Order Rename(RenameOrder command, Order current);
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

`[DomainFactory]` is the DDD-focused difference. `Input.Members` binds command members to a factory by name; `Input.Source` sends one complete value into a strongly typed ID or value-object factory. The factory is a required boundary: if DomainMap cannot satisfy it, compilation fails instead of silently choosing a public constructor or assigning properties.

## Design boundary

DomainMap owns data movement. Your domain owns behavior.

- Use generated mappings for commands, integration contracts, persistence models, read models, and projections.
- Use `[DomainFactory]` to enter an aggregate through a constructor or named factory.
- Use `DomainFactoryInput.Source` for strongly typed IDs and value objects.
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

The suite retains Mapperly's broad conformance corpus and adds DomainMap-specific generator and runtime tests for required factory binding, strongly typed IDs, immutable aggregate updates, explicit failure results, projection rejection, and invariant failures.

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
