---
sidebar_position: 1
description: Preserve aggregate and value-object invariants with required domain factories.
---

# Domain boundaries

`DomainFactoryAttribute` marks a construction path that DomainMap must use. The generator moves data to that boundary; the method remains ordinary user-owned C# and delegates to your domain model.

## Enter an aggregate

The default `Input.Members` mode binds source members to factory parameters by name and applies normal mapping conversions to each value.

```csharp
[DomainMapper]
public static partial class OrdersMap
{
    [DomainFactory]
    private static Order Create(
        OrderId id,
        CustomerName customerName,
        OrderTotal total)
        => Order.Place(id, customerName, total);

    public static partial Order Map(PlaceOrder source);
}
```

The factory owns the complete target. DomainMap does not append an object initializer or mutate members after it returns.

## Construct a strongly typed value

Use `Input.Source` when the complete source value should be passed to a one-parameter factory.

```csharp
[DomainFactory(Input = DomainFactoryInput.Source)]
private static OrderId ToOrderId(Guid value) => OrderId.Create(value);

[DomainFactory(Input = DomainFactoryInput.Source)]
private static CustomerName ToCustomerName(string value)
    => CustomerName.Create(value);
```

This makes validation visible in the mapping API and reusable when the value appears inside an aggregate mapping.

## Apply an immutable change

Additional mapping parameters can carry the current aggregate into the boundary. Returning a new aggregate keeps the state transition in domain behavior.

```csharp
[DomainFactory]
private static Order Rename(Order current, CustomerName customerName)
    => current.Rename(customerName);

public static partial Order Rename(RenameOrder source, Order current);
```

DomainMap binds `customerName` from the command and `current` from the additional parameter. It does not infer a state transition from property names.

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
- `Input.Source` requires exactly one factory parameter.

Use `[ObjectFactory(MapToParameters = true)]` instead when construction is an optional infrastructure customization and ordinary construction is an acceptable fallback.
