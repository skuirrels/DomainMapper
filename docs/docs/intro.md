---
sidebar_position: 0
description: Introduction into DomainMap.
---

# Introduction

**Map data. Preserve intent.**

DomainMap is an experimental compile-time mapper for .NET with a domain-driven design bias. It generates readable C# without runtime reflection and keeps aggregate construction, value-object validation, and invariants in user-owned domain code.

DomainMap is an independent Apache-2.0 derivative of [Mapperly](https://github.com/riok/mapperly). It retains Mapperly's broad structural mapping engine while exploring a domain-first API.

## The domain boundary is the API

```csharp
using DomainMap.Abstractions;

[DomainMapper]
public static partial class OrdersMap
{
    [DomainFactory]
    private static Order Create(OrderId id, CustomerName customerName, Money total)
        => Order.Place(id, customerName, total);

    private static OrderId ToOrderId(Guid value) => new(value);
    private static CustomerName ToCustomerName(string value) => CustomerName.Create(value);
    private static Money ToMoney(decimal value) => Money.Gbp(value);

    public static partial Order ToDomain(PlaceOrder command);
    public static partial OrderView ToView(Order order);
}
```

`[DomainFactory]` binds source members to the factory parameters by name. The generated mapper moves data to the boundary; the factory remains ordinary domain code and owns validation.

## Design principles

- Generated code owns mechanical data movement.
- The domain owns construction rules and behavior.
- Strongly typed IDs and value objects use small, explicit conversion methods.
- Aggregate state changes that carry business meaning remain explicit domain method calls.
- Generated output stays readable, debuggable, trimming-safe, and AOT-friendly.

## Status

DomainMap is currently source-built and experimental; it is not yet a published NuGet product. Start with the [installation guide](/docs/getting-started/installation), then explore the inherited mapping capabilities under **Usage and configuration**.
