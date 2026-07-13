---
sidebar_position: 14
description: Use reference handling to handle reference loops
---

# Reference handling

DomainMap can support mapping object structures with circular references.
To opt in for reference handling set `UseReferenceHandling` to `true`:

```csharp
// highlight-start
[DomainMapper(UseReferenceHandling = true)]
// highlight-end
public partial class CarMapper
{
    public partial CarDto CarToCarDto(Car car);
}
```

This enables the usage of a default reference handler
which reuses the same target object instance if encountered the same source object instance.

:::info
When using reference handling, the DomainMap package reference needs to include the runtime assets
(the runtime assets are needed for the reference handler implementation).  
Make sure `ExcludeAssets` on the `PackageReference` does not include `runtime` when using reference handling.
:::

## Custom reference handler

To use a custom `IReferenceHandler` implementation,
a parameter of the type `DomainMap.Abstractions.ReferenceHandling.IReferenceHandler`
annotated with the `DomainMap.Abstractions.ReferenceHandling.ReferenceHandlerAttribute`
can be added to the mapping method.

```csharp
// highlight-start
[DomainMapper(UseReferenceHandling = true)]
// highlight-end
public partial class CarMapper
{
    // highlight-start
    public partial CarDto CarToCarDto(Car car, [ReferenceHandler] IReferenceHandler myRefHandler);
    // highlight-end
}
```

## User implemented mappings

To make use of the `IReferenceHandler` in a user implemented mapping method,
add a parameter as described in the section "[Custom reference handler](#custom-reference-handler)".
