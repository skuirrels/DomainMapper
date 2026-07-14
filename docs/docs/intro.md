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
    [MapToFactory(nameof(Order.Place))]
    public static partial Order ToDomain(this PlaceOrder command);

    public static partial OrderDto ToDto(this Order order);
}
```

`[MapToFactory]` binds source members directly to a target-owned aggregate factory. Conventional one-argument `Create` methods construct strongly typed IDs and value objects automatically. These are required boundaries: an unsatisfied factory produces a compile-time error instead of falling back to property assignment.

## Design principles

- Generated code owns mechanical data movement.
- The domain owns construction rules and behavior.
- Strongly typed IDs and value objects use small, explicit domain factories.
- Aggregate state changes that carry business meaning receive the current aggregate and delegate to a domain method.
- Generated output stays readable, debuggable, trimming-safe, and AOT-friendly.

## Status

DomainMap is currently source-built and experimental; it is not yet a published NuGet product. Start with the [installation guide](/docs/getting-started/installation), then read [domain boundaries](/docs/configuration/domain-boundaries) before exploring the inherited mapping capabilities.
