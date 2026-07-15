---
sidebar_position: 0
description: Introduction into DomainMap.
---

# Introduction

**Map data. Preserve intent.**

DomainMap is a compile-time mapper for .NET with a domain-driven design bias. It generates readable C# without runtime reflection and keeps aggregate construction, value-object validation, and invariants in user-owned domain code.

Its domain-first API keeps construction and aggregate invariants under domain ownership.

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

DomainMap v1.0.0 is available as a GitHub source release; NuGet publishing is intentionally deferred. Start with the [installation guide](/docs/getting-started/installation), then read [domain boundaries](/docs/configuration/domain-boundaries) before exploring its structural mapping capabilities.
