namespace DomainMapper.Sample;

public readonly record struct OrderId(int Value);

public sealed class Order
{
    private Order(OrderId id, string customerName, decimal total)
    {
        Id = id;
        CustomerName = customerName;
        Total = total;
    }

    public OrderId Id { get; }

    public string CustomerName { get; }

    public decimal Total { get; }

    public static Order Place(OrderId id, string customerName, decimal total)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerName);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        return new Order(id, customerName, total);
    }
}
