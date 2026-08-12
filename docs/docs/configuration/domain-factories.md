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

The named factory must be static, accessible, and return the mapping target. Every factory parameter must have a same-named source property. A `[DomainFactory]` method can convert a source property to the factory parameter type without exposing mutable domain state.
