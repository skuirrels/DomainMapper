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
    public void FallsBackWhenRequiredFactoryMemberIsMissing()
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
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = new global::B();
                target.Value = source.Value;
                return target;
                """
            );
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
