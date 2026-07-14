---
sidebar_position: 1
description: Preserve aggregate and value-object invariants with required domain factories.
---

# Domain boundaries

`MapToFactoryAttribute` selects a target-owned static factory that DomainMap must use. The generator moves data to that boundary while the domain model continues to own construction and invariants.

## Enter an aggregate

`MapToFactory` binds source members to factory parameters by name and applies normal mapping conversions to each value.

```csharp
[DomainMapper]
public static partial class OrdersMap
{
    [MapToFactory(nameof(Order.Place))]
    public static partial Order Map(this PlaceOrder source);

    public static partial OrderDto ToDto(this Order source);
}
```

Given `Order.Place(OrderId id, CustomerName customerName, OrderTotal total)`, DomainMap maps the command members to those parameters. The factory owns the complete target; DomainMap does not append an object initializer or mutate members after it returns. If a nullable factory unexpectedly returns `null`, DomainMap throws instead of constructing a fallback object.

## Construct a strongly typed value

DomainMap already recognizes conventional one-argument static conversion methods such as `Create`, `CreateFrom`, and `From`. No mapper adapters are required for the common value-object shape:

```csharp
public static OrderId Create(Guid value);
public static CustomerName Create(string value);
public static OrderTotal Create(decimal value);
```

The generated aggregate call is direct and inspectable:

```csharp
return Order.Place(
    OrderId.Create(source.Id),
    CustomerName.Create(source.CustomerName),
    OrderTotal.Create(source.Total));
```

For a non-conventional conversion or a factory that receives the complete source value, define a mapper method with `[DomainFactory(Input = DomainFactoryInput.Source)]`.

## Apply an immutable change

Additional mapping parameters can carry the current aggregate into the boundary. Returning a new aggregate keeps the state transition in domain behavior.

```csharp
[DomainFactory]
private static Order Rename(Order current, CustomerName customerName)
    => current.Rename(customerName);

public static partial Order Rename(RenameOrder source, Order current);
```

DomainMap binds `customerName` from the command and `current` from the additional parameter. It does not infer a state transition from property names.

`[DomainFactory]` remains the explicit advanced API for mapper-owned boundaries. Unlike `[MapToFactory]`, it marks a method on the mapper rather than naming a static method on the target type.

## Keep failures explicit

A domain factory may throw for invalid programmer or input state, or return your application's own result type for expected failures. DomainMap propagates either contract unchanged and adds no runtime wrapper.

```csharp
[DomainFactory]
private static PlacementResult TryCreate(Guid id, string customerName, decimal total)
{
    try
    {
        return PlacementResult.Success(
            Order.Place(OrderId.Create(id), CustomerName.Create(customerName), OrderTotal.Create(total))
        );
    }
    catch (ArgumentException ex)
    {
        return PlacementResult.Failure(ex.Message);
    }
}

public static partial PlacementResult TryMap(PlaceOrder source);
```

## Compile-time guardrails

- `DMAP001` is an error when a required factory cannot bind its required parameters. No constructor or property-assignment fallback is generated.
- `DMAP002` is an error when an aggregate factory would be used inside an `IQueryable` projection. Project to a read model, then enter the domain boundary outside the query provider.
- `DMAP003` is an error when `[MapToFactory]` cannot find an accessible, non-generic static method with the configured name that returns the target type.
- `Input.Source` requires exactly one factory parameter.

Use `[ObjectFactory(MapToParameters = true)]` instead when construction is an optional infrastructure customization and ordinary construction is an acceptable fallback.
