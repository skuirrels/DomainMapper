using DomainMap.Diagnostics;

namespace DomainMap.Tests.Mapping;

public class MapToFactoryTest
{
    [Fact]
    public void MapsDirectlyToTargetOwnedFactoryWithoutMapperAdapters()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(Order.Place))]
            partial Order Map(CreateOrder source);
            """,
            "record CreateOrder(Guid Id, string CustomerName, decimal Total);",
            "record OrderId { private OrderId(Guid value) {} public static OrderId Create(Guid value) => new(value); }",
            "record CustomerName { private CustomerName(string value) {} public static CustomerName Create(string value) => new(value); }",
            "record Money { private Money(decimal value) {} public static Money Create(decimal value) => new(value); }",
            "class Order { private Order() {} public static Order Place(OrderId id, CustomerName customerName, Money total) => new(); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = global::Order.Place(global::OrderId.Create(source.Id), global::CustomerName.Create(source.CustomerName), global::Money.Create(source.Total));
                return target;
                """
            );
    }

    [Fact]
    public void TargetFactoryOwnsTheCompleteAggregate()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            partial B Map(A source);
            """,
            "record A(string Name);",
            "class B { public string Name { get; set; } = string.Empty; public string FactoryOwned { get; set; } = string.Empty; public static B Create(string name) => new() { Name = name, FactoryOwned = \"owned\" }; }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = global::B.Create(source.Name);
                return target;
                """
            );
    }

    [Fact]
    public void PreservesOptionalFactoryParameters()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            partial B Map(A source);
            """,
            "record A(string Name, int Quantity);",
            "record B(string Name, string Currency, int Quantity) { public static B Create(string name, string currency = \"GBP\", int quantity = 0) => new(name, currency, quantity); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = global::B.Create(source.Name, quantity: source.Quantity);
                return target;
                """
            );
    }

    [Fact]
    public void SelectsTheSatisfiableFactoryOverload()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            partial B Map(A source);
            """,
            "record A(string Name);",
            "class B { private B() {} public static B Create(int missing) => new(); public static B Create(string name) => new(); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = global::B.Create(source.Name);
                return target;
                """
            );
    }

    [Fact]
    public void ExplicitFactoryWinsOverConventionalStaticConversion()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Place))]
            partial B Map(A source);
            """,
            "record A(string Name);",
            "class B { private B() {} public static B Create(A source) => new(); public static B Place(string name) => new(); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = global::B.Place(source.Name);
                return target;
                """
            );
    }

    [Fact]
    public void ExplicitFactoryWinsOverDirectAssignment()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Rehydrate))]
            partial B Map(B source);
            """,
            "record B(string Name) { public static B Rehydrate(string name) => new(name); }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = global::B.Rehydrate(source.Name);
                return target;
                """
            );
    }

    [Fact]
    public void GuardsNullableMembersBeforeFactoryCall()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            partial B Map(A source);
            """,
            "record A(string? Name);",
            "record B(string Name) { public static B Create(string name) => new(name); }"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.NullableSourceValueToNonNullableTargetValue)
            .HaveSingleMethodBody(
                """
                var target = global::B.Create(source.Name ?? throw new global::System.ArgumentNullException(nameof(source.Name)));
                return target;
                """
            )
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void DoesNotConstructFallbackWhenFactoryReturnsNull()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            partial B Map(A source);
            """,
            "record A(string Name);",
            "class B { public B() {} public static B? Create(string name) => null; }"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveSingleMethodBody(
                """
                var target = global::B.Create(source.Name) ?? throw new global::System.NullReferenceException("The domain factory Create returned null");
                return target;
                """
            );
    }

    [Fact]
    public void ReportsMissingFactoryWithoutFallingBack()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory("Missing")]
            partial B Map(A source);
            """,
            "record A(string Name);",
            "class B { public B() {} public string Name { get; set; } = string.Empty; }"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(
                DiagnosticDescriptors.ConfiguredDomainFactoryNotFound,
                "The configured domain factory B.Missing was not found or has an invalid signature. It must be an accessible, non-generic static method returning B."
            )
            .HaveSingleMethodBody(
                """
                var target = default(global::B)!;
                return target;
                """
            )
            .HaveAssertedAllDiagnostics();
    }

    [Theory]
    [InlineData("public B Create(string name) => new();")]
    [InlineData("public static A Create(string name) => new(name);")]
    [InlineData("public static B Create<T>(string name) => new();")]
    [InlineData("public static async Task<B> Create(string name) => await Task.FromResult(new B());")]
    [InlineData("private static B Create(string name) => new();")]
    public void ReportsInvalidFactorySignature(string factory)
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory("Create")]
            partial B Map(A source);
            """,
            "record A(string Name);",
            $"class B {{ public B() {{ }} {factory} }}"
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.ConfiguredDomainFactoryNotFound)
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public void ReportsUnsatisfiedFactoryWithoutConstructorFallback()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            partial B Map(A source);
            """,
            "record A(string Name);",
            "class B { public B() {} public static B Create(string missing) => new(); }"
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
    public void RejectsFactoryBoundaryInProjection()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            public partial System.Linq.Expressions.Expression<System.Func<A, B>> Map();
            """,
            "record A(string Name);",
            "record B(string Name) { public static B Create(string name) => new(name); }"
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
    public void RejectsFactoryBoundaryInQueryableProjection()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            public partial IQueryable<B> Map(IQueryable<A> source);
            """,
            "record A(string Name);",
            "record B(string Name) { public static B Create(string name) => new(name); }"
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
    public void DoesNotAffectUnconfiguredMappings()
    {
        var source = TestSourceBuilder.MapperWithBodyAndTypes(
            """
            [MapToFactory(nameof(B.Create))]
            partial B MapBoundary(A source);

            partial C MapDto(A source);
            """,
            "record A(string Name);",
            "record B(string Name) { public static B Create(string name) => new(name); }",
            "record C(string Name);"
        );

        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMethodBody(
                "MapBoundary",
                """
                var target = global::B.Create(source.Name);
                return target;
                """
            )
            .HaveMethodBody(
                "MapDto",
                """
                var target = new global::C(source.Name);
                return target;
                """
            );
    }
}
