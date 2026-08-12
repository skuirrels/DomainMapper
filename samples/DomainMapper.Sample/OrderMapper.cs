using DomainMapper.Abstractions;

namespace DomainMapper.Sample;

[DomainMapper]
public static partial class OrderMapper
{
    [DomainFactory(Input = DomainFactoryInput.Source)]
    private static OrderId ToOrderId(int value) => new(value);

    [MapToFactory(nameof(Order.Place))]
    public static partial Order Place(OrderDraft source);
}
