using System;
using DomainMap.Abstractions;
using Shouldly;
using Xunit;

namespace DomainMap.IntegrationTests
{
    public class DomainFactoryMapperTest
    {
        [Fact]
        public void CreatesAggregateThroughItsInvariantBoundary()
        {
            var command = new PlaceOrder(Guid.NewGuid(), "Ada Lovelace", 42.50m);

            var order = OrderDomainMap.Map(command);

            order.Id.Value.ShouldBe(command.Id);
            order.CustomerName.ShouldBe(command.CustomerName);
            order.Total.ShouldBe(command.Total);
        }

        [Fact]
        public void DoesNotBypassFactoryValidation()
        {
            var command = new PlaceOrder(Guid.NewGuid(), "Ada Lovelace", -1m);

            Should.Throw<ArgumentOutOfRangeException>(() => OrderDomainMap.Map(command));
        }
    }

    public sealed record PlaceOrder(Guid Id, string CustomerName, decimal Total);

    public readonly record struct OrderId(Guid Value);

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

    [DomainMapper]
    public static partial class OrderDomainMap
    {
        [DomainFactory]
        private static Order Create(OrderId id, string customerName, decimal total) => Order.Place(id, customerName, total);

        private static OrderId ToOrderId(Guid value) => new(value);

        public static partial Order Map(PlaceOrder source);
    }
}
