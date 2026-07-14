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
            order.CustomerName.Value.ShouldBe(command.CustomerName);
            order.Total.Value.ShouldBe(command.Total);
        }

        [Fact]
        public void MapsAggregateBackToDtoWithoutBoundaryAdapters()
        {
            var command = new PlaceOrder(Guid.NewGuid(), "Ada Lovelace", 42.50m);

            var dto = OrderDomainMap.ToDto(OrderDomainMap.Map(command));

            dto.ShouldBe(new OrderDto(command.Id, command.CustomerName, command.Total));
        }

        [Fact]
        public void DoesNotBypassFactoryValidation()
        {
            var command = new PlaceOrder(Guid.NewGuid(), "Ada Lovelace", -1m);

            Should.Throw<ArgumentOutOfRangeException>(() => OrderDomainMap.Map(command));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void DoesNotBypassRequiredCustomerNameValidation(string? customerName)
        {
            var command = new PlaceOrder(Guid.NewGuid(), customerName!, 42.50m);

            Should.Throw<ArgumentException>(() => OrderDomainMap.Map(command));
        }

        [Fact]
        public void DoesNotBypassStrongIdValidation()
        {
            var command = new PlaceOrder(Guid.Empty, "Ada Lovelace", 42.50m);

            Should.Throw<ArgumentException>(() => OrderDomainMap.Map(command));
        }

        [Fact]
        public void AppliesImmutableChangeThroughDomainBehavior()
        {
            var original = OrderDomainMap.Map(new PlaceOrder(Guid.NewGuid(), "Ada Lovelace", 42.50m));

            var renamed = OrderDomainMap.Rename(new RenameOrder("Grace Hopper"), original);

            renamed.ShouldNotBeSameAs(original);
            original.CustomerName.Value.ShouldBe("Ada Lovelace");
            renamed.CustomerName.Value.ShouldBe("Grace Hopper");
            renamed.Id.ShouldBe(original.Id);
            renamed.Total.ShouldBe(original.Total);
        }

        [Fact]
        public void PreservesExplicitFailureResultFromDomainBoundary()
        {
            var result = OrderDomainMap.TryMap(new PlaceOrder(Guid.NewGuid(), "Ada Lovelace", -1m));

            result.IsSuccess.ShouldBeFalse();
            result.Order.ShouldBeNull();
            result.Error.ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        public void PreservesExplicitSuccessResultFromDomainBoundary()
        {
            var result = OrderDomainMap.TryMap(new PlaceOrder(Guid.NewGuid(), "Ada Lovelace", 42.50m));

            result.IsSuccess.ShouldBeTrue();
            result.Order.ShouldNotBeNull();
            result.Error.ShouldBeNull();
        }
    }

    public sealed record PlaceOrder(Guid Id, string CustomerName, decimal Total);

    public sealed record OrderDto(Guid Id, string CustomerName, decimal Total);

    public sealed record RenameOrder(string CustomerName);

    public sealed record OrderId
    {
        private OrderId(Guid value)
        {
            Value = value;
        }

        public Guid Value { get; }

        public static OrderId Create(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("An order identifier cannot be empty.", nameof(value));

            return new OrderId(value);
        }

        public static implicit operator Guid(OrderId value) => value.Value;
    }

    public sealed record CustomerName
    {
        private CustomerName(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static CustomerName Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A customer name is required.", nameof(value));

            return new CustomerName(value);
        }

        public static implicit operator string(CustomerName value) => value.Value;
    }

    public sealed record OrderTotal
    {
        private OrderTotal(decimal value)
        {
            Value = value;
        }

        public decimal Value { get; }

        public static OrderTotal Create(decimal value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "An order total cannot be negative.");

            return new OrderTotal(value);
        }

        public static implicit operator decimal(OrderTotal value) => value.Value;
    }

    public sealed class Order
    {
        private Order(OrderId id, CustomerName customerName, OrderTotal total)
        {
            Id = id;
            CustomerName = customerName;
            Total = total;
        }

        public OrderId Id { get; }

        public CustomerName CustomerName { get; }

        public OrderTotal Total { get; }

        public static Order Place(OrderId id, CustomerName customerName, OrderTotal total) => new(id, customerName, total);

        public Order Rename(CustomerName customerName) => new(Id, customerName, Total);
    }

    public sealed record PlacementResult
    {
        private PlacementResult(bool isSuccess, Order? order, string? error)
        {
            IsSuccess = isSuccess;
            Order = order;
            Error = error;
        }

        public bool IsSuccess { get; }

        public Order? Order { get; }

        public string? Error { get; }

        public static PlacementResult Success(Order order) => new(true, order, null);

        public static PlacementResult Failure(string error) => new(false, null, error);
    }

    [DomainMapper]
    public static partial class OrderDomainMap
    {
        [DomainFactory]
        private static Order Rename(Order current, CustomerName customerName) => current.Rename(customerName);

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

        [MapToFactory(nameof(Order.Place))]
        public static partial Order Map(PlaceOrder source);

        public static partial Order Rename(RenameOrder source, Order current);

        public static partial PlacementResult TryMap(PlaceOrder source);

        public static partial OrderDto ToDto(Order source);
    }
}
