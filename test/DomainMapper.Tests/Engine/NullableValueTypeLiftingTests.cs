namespace DomainMapper.Tests.Engine;

public sealed class NullableValueTypeLiftingTests
{
    [Fact]
    public void LiftsNullableValueTypesThroughDomainFactories()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static ExternalRef ToRef(int value) => new(value);

                [MappingCompleteness(MappingCompleteness.Both)]
                public static partial Order Map(OrderDto source);

                public static string Run() =>
                    (Map(new OrderDto(null)).Ref is null ? "none" : "some") + "|" + Map(new OrderDto(5)).Ref!.Value.Value;
            }

            public readonly record struct ExternalRef(int Value);
            public sealed record OrderDto(int? Ref);
            public sealed record Order(ExternalRef? Ref);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("source.Ref is null ? default(global::ExternalRef?) : ToRef((source.Ref).Value)");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("none|5");
    }

    [Fact]
    public void LiftsNullableValueTypesThroughSingleValueConstructorsAndConventionHelpers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Customer Map(CustomerDto source);

                public static string Run()
                {
                    var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
                    var mapped = Map(new CustomerDto(id, new PointDto(1, 2)));
                    var empty = Map(new CustomerDto(null, null));
                    return $"{mapped.Id!.Value.Value}|{mapped.Home!.Value.X},{mapped.Home!.Value.Y}|{empty.Id is null}|{empty.Home is null}";
                }
            }

            public readonly record struct CustomerId(Guid Value);
            public readonly record struct PointDto(int X, int Y);
            public readonly record struct Point(int X, int Y);
            public sealed record CustomerDto(Guid? Id, PointDto? Home);
            public sealed record Customer(CustomerId? Id, Point? Home);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("new global::CustomerId((source.Id).Value)");
        result.Source.ShouldContain("MapToPoint((source.Home).Value)");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("11111111-1111-1111-1111-111111111111|1,2|True|True");
    }

    [Fact]
    public void LiftsNullableValueTypesIntoNullableReferenceTargets()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Line Map(LineDto source);

                public static string Run() => (Map(new LineDto(null)).Quantity is null ? "none" : "some") + "|" + Map(new LineDto(3)).Quantity!.Value;
            }

            public sealed class Quantity
            {
                public Quantity(int value) => Value = value;
                public int Value { get; }
            }
            public sealed record LineDto(int? Quantity);
            public sealed record Line(Quantity? Quantity);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("source.Quantity is null ? null : new global::Quantity((source.Quantity).Value)");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("none|3");
    }

    [Fact]
    public void LiftsNullableReferenceSourcesIntoNullableValueTypeTargets()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Order Map(OrderDto source);

                public static string Run() => (Map(new OrderDto(null)).Code is null ? "none" : "some") + "|" + Map(new OrderDto("SAVE")).Code!.Value.Value;
            }

            public readonly record struct DiscountCode(string Value);
            public sealed record OrderDto(string? Code);
            public sealed record Order(DiscountCode? Code);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("source.Code is null ? default(global::DiscountCode?) : new global::DiscountCode(source.Code!)");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("none|SAVE");
    }

    [Fact]
    public void KeepsImplicitLiftedConversionsDirect()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int? Value);
            public sealed record Target(long? Value);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("new global::Target(source.Value)");
        result.Source.ShouldNotContain("is null");
    }

    [Fact]
    public void StillRequiresAPolicyForNullableValueTypesIntoNonNullableTargets()
    {
        var rejected = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static ExternalRef ToRef(int value) => new(value);

                public static partial Order Map(OrderDto source);
            }

            public readonly record struct ExternalRef(int Value);
            public sealed record OrderDto(int? Ref);
            public sealed record Order(ExternalRef Ref);
            """
        );

        rejected.Errors.ShouldContain(x => x.Id == "DMPR101");
        rejected.Source.ShouldBeEmpty();

        var guarded = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static ExternalRef ToRef(int value) => new(value);

                [MapNull(nameof(Order.Ref), NullMemberBehavior.Throw)]
                public static partial Order Map(OrderDto source);
            }

            public readonly record struct ExternalRef(int Value);
            public sealed record OrderDto(int? Ref);
            public sealed record Order(ExternalRef Ref);
            """
        );

        guarded.Errors.ShouldBeEmpty(guarded.Source);
        guarded.Source.ShouldContain("throw new global::System.InvalidOperationException");
    }

    [Fact]
    public void DoesNotBindNullableValueOrHasValueByConvention()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int? Amount);
            public sealed record Target(Snapshot Amount);
            public sealed record Snapshot(bool HasValue, int Value);
            """
        );

        result.Errors.ShouldContain(x => x.Id == "DMPR101");
        result.Source.ShouldBeEmpty();
    }
}
