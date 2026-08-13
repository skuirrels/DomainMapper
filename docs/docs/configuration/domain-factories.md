# Domain factories

Use `[MapToFactory]` when entering an aggregate:

```csharp
[DomainMapper]
public static partial class OrderMapper
{
    [DomainFactory(Input = DomainFactoryInput.Source)]
    private static OrderId ToOrderId(int value) => new(value);

    [MapToFactory(nameof(Order.Place))]
    public static partial Order Place(OrderDraft source);
}
```

The named factory must be static, accessible, and return the mapping target. Every factory parameter must bind by name to either a source property or an additional mapping-method parameter. An explicit mapping parameter wins when it has the same name as a root source property.

`[DomainFactory]` supports two input modes:

- `DomainFactoryInput.Source` passes the complete source value to exactly one factory parameter.
- `DomainFactoryInput.Members` binds factory parameters from same-named members of the value being converted, then from additional mapping parameters. Members of the value being converted take precedence over ambient parameters.

Domain factories must be static, non-generic, and return a value. These rules keep generated calls valid from static mapping methods and prevent the generator from bypassing domain construction.
