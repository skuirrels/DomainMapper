using DomainMap.Abstractions;
using DomainMap.Diagnostics;

namespace DomainMap.Tests.Mapping;

public class DomainFactoryTest
{
    [Fact]
    public void BindsSourceMembersWithoutConfigurationCeremony()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private B Create(string name, int quantity) => B.Create(name, quantity);

            partial B Map(A source);
            """,
            "record A(string Name, int Quantity);",
            "class B { private B() {} public static B Create(string name, int quantity) => new(); public string Name => string.Empty; public int Quantity => 0; }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create(source.Name, source.Quantity);
                return target;
                """
            );
    }

    [Fact]
    public void PreservesOptionalFactoryParameters()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private B Create(string name, string currency = "GBP", int quantity = 0) => new(name, currency, quantity);

            [MapperIgnoreTarget(nameof(B.Currency))]
            partial B Map(A source);
            """,
            "record A(string Name, int Quantity);",
            "record B(string Name, string Currency, int Quantity);"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create(source.Name, quantity: source.Quantity);
                return target;
                """
            );
    }

    [Fact]
    public void MapsStronglyTypedIdsBeforeCallingFactory()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            private OrderId ToOrderId(Guid value) => new(value);

            [DomainFactory]
            private Order Create(OrderId id, string customerName) => Order.Create(id, customerName);

            partial Order Map(CreateOrder source);
            """,
            "record CreateOrder(Guid Id, string CustomerName);",
            "readonly record struct OrderId(Guid Value);",
            "class Order { private Order() {} public static Order Create(OrderId id, string customerName) => new(); public OrderId Id => default; public string CustomerName => string.Empty; }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create(ToOrderId(source.Id), source.CustomerName);
                return target;
                """
            );
    }

    [Fact]
    public void PassesWholeSourceToValueObjectFactory()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory(Input = DomainFactoryInput.Source)]
            private OrderId CreateOrderId(Guid value) => OrderId.Create(value);

            [DomainFactory]
            private Order Create(OrderId id) => new(id);

            partial Order Map(CreateOrder source);
            """,
            "record CreateOrder(Guid Id);",
            "record OrderId(Guid Value) { public static OrderId Create(Guid value) => new(value); }",
            "record Order(OrderId Id);"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create(CreateOrderId(source.Id));
                return target;
                """
            );
    }

    [Fact]
    public void PassesWholeSourceToRootDomainFactory()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory(Input = DomainFactoryInput.Source)]
            private B Create(A source) => B.Create(source.Value);

            partial B Map(A source);
            """,
            "record A(string Value);",
            "record B(string Value) { public static B Create(string value) => new(value); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create(source);
                return target;
                """
            );
    }

    [Fact]
    public void HonorsCaseInsensitiveMemberMatching()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private B Create(string externalid) => new(externalid);

            partial B Map(A source);
            """,
            new TestSourceBuilderOptions { PropertyNameMappingStrategy = PropertyNameMappingStrategy.CaseInsensitive },
            "record A(string ExternalId);",
            "record B(string ExternalId);"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create(source.ExternalId);
                return target;
                """
            );
    }

    [Fact]
    public void GuardsNullableMembersBeforeInvariantFactoryCall()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private B Create(string name) => new(name);

            partial B Map(A source);
            """,
            "record A(string? Name);",
            "record B(string Name);"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.NullableSourceValueToNonNullableTargetValue)
            .HaveSingleMethodBody(
                """
                var target = Create(source.Name ?? throw new global::System.ArgumentNullException(nameof(source.Name)));
                return target;
                """
            )
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void SelectsFactoryByAggregateReturnType()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private B CreateB(string value) => new(value);

            [DomainFactory]
            private C CreateC(string value) => new(value);

            partial B MapB(A source);
            partial C MapC(A source);
            """,
            "record A(string Value);",
            "record B(string Value);",
            "record C(string Value);"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMethodBody(
                "MapB",
                """
                var target = CreateB(source.Value);
                return target;
                """
            )
            .HaveMethodBody(
                "MapC",
                """
                var target = CreateC(source.Value);
                return target;
                """
            );
    }

    [Fact]
    public void SupportsGenericAggregateFactories()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private TAggregate Create<TAggregate>(string value) where TAggregate : Aggregate, new()
                => new TAggregate();

            partial ConcreteAggregate Map(A source);
            """,
            "record A(string Value);",
            "class Aggregate { public string Value { get; set; } = string.Empty; }",
            "class ConcreteAggregate : Aggregate { }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create<global::ConcreteAggregate>(source.Value);
                return target;
                """
            );
    }

    [Fact]
    public void DoesNotFallBackWhenRequiredFactoryMemberIsMissing()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private B Create(string missing) => new();

            partial B Map(A source);
            """,
            "record A(string Value);",
            "class B { public string Value { get; set; } = string.Empty; }"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(
                DiagnosticDescriptors.DomainFactoryCannotBeSatisfied,
                "The domain factory Create cannot construct B from A. Required parameters could not be mapped: missing."
            )
            .HaveSingleMethodBody(
                """
                var target = default(global::B)!;
                return target;
                """
            )
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void DoesNotFallBackToOrdinaryObjectFactory()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private B CreateDomain(string missing) => new();

            [ObjectFactory]
            private B CreateObject() => new();

            partial B Map(A source);
            """,
            "record A(string Value);",
            "class B { public string Value { get; set; } = string.Empty; }"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.DomainFactoryCannotBeSatisfied)
            .HaveSingleMethodBody(
                """
                var target = default(global::B)!;
                return target;
                """
            )
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void TreatsFactoryResultAsCompleteAggregate()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private B Create(string name) => B.Create(name);

            partial B Map(A source);
            """,
            "record A(string Name);",
            "class B { private B(string name) { Name = name; MutableDetail = \"owned\"; } public string Name { get; } public string MutableDetail { get; set; } public static B Create(string name) => new(name); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create(source.Name);
                return target;
                """
            );
    }

    [Fact]
    public void BindsAdditionalParameterForImmutableUpdate()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private Order Rename(Order current, string customerName) => current.Rename(customerName);

            partial Order Map(RenameOrder source, Order current);
            """,
            "record RenameOrder(string CustomerName);",
            "record Order(string CustomerName) { public Order Rename(string customerName) => new(customerName); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Rename(current, source.CustomerName);
                return target;
                """
            );
    }

    [Fact]
    public void PreservesUserOwnedResultFailureContract()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private PlacementResult TryCreate(string name, int quantity) => PlacementResult.TryCreate(name, quantity);

            partial PlacementResult Map(PlaceOrder source);
            """,
            "record PlaceOrder(string Name, int Quantity);",
            "record PlacementResult(bool IsSuccess) { public static PlacementResult TryCreate(string name, int quantity) => new(quantity > 0); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = TryCreate(source.Name, source.Quantity);
                return target;
                """
            );
    }

    [Fact]
    public void RejectsMemberBoundDomainFactoryInProjection()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            public partial System.Linq.Expressions.Expression<System.Func<A, B>> Map();

            [DomainFactory]
            private B Create(string value) => new(value);
            """,
            "record A(string Value);",
            "record B(string Value);"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(
                DiagnosticDescriptors.DomainFactoryCannotBeUsedInProjection,
                "The domain factory Create cannot construct B in a queryable projection. Project to a read model instead."
            )
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void RejectsWholeSourceDomainFactoryInProjection()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            public partial System.Linq.Expressions.Expression<System.Func<A, B>> Map();

            [DomainFactory(Input = DomainFactoryInput.Source)]
            private B Create(A source) => new(source.Value);
            """,
            "record A(string Value);",
            "record B(string Value);"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.DomainFactoryCannotBeUsedInProjection)
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void RejectsWholeSourceFactoryWithoutExactlyOneParameter()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory(Input = DomainFactoryInput.Source)]
            private B Create(string value, int quantity) => new(value, quantity);

            partial B Map(A source);
            """,
            "record A(string Value, int Quantity);",
            "record B(string Value, int Quantity);"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature)
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void RejectsWholeSourceFactoryWithoutAParameter()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory(Input = DomainFactoryInput.Source)]
            private B Create() => new("value");

            partial B Map(A source);
            """,
            "record A(string Value);",
            "record B(string Value);"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature)
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void SupportsProtectedFactoryOnDomainMapperBaseClass()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            abstract class DomainMapBase
            {
                [DomainFactory]
                protected B Create(string value) => new(value);
            }

            [DomainMapper]
            partial class Mapper : DomainMapBase
            {
                public partial B Map(A source);
            }

            record A(string Value);
            record B(string Value);
            """
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = Create(source.Value);
                return target;
                """
            );
    }

    [Fact]
    public void RejectsAsyncDomainFactory()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [DomainFactory]
            private async Task<B> Create(string value) => await Task.FromResult(new B(value));

            partial B Map(A source);
            """,
            "record A(string Value);",
            "record B(string Value);"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature)
            .HaveAssertedAllDiagnostics();
    }
}
