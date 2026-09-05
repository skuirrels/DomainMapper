using DomainMapper.Projections;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Tests.Engine;

public sealed class EnumMappingTests
{
    [Fact]
    public void MapsEnumsByMemberNameWithAThrowingDefault()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Order Map(OrderDto source);

                public static string Run()
                {
                    var mapped = $"{Map(new OrderDto(1, OrderStatusDto.Pending)).Status}|{Map(new OrderDto(2, OrderStatusDto.PAID)).Status}";
                    try
                    {
                        Map(new OrderDto(3, (OrderStatusDto)42));
                        return mapped + "|accepted";
                    }
                    catch (InvalidOperationException e)
                    {
                        return mapped + "|" + e.Message;
                    }
                }
            }

            public enum OrderStatusDto { Pending, PAID }
            public enum OrderStatus { Unknown, Pending, Paid, Shipped }
            public sealed record OrderDto(int Id, OrderStatusDto Status);
            public sealed record Order(int Id, OrderStatus Status);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("global::OrderStatusDto.Pending => global::OrderStatus.Pending,");
        result.Source.ShouldContain("global::OrderStatusDto.PAID => global::OrderStatus.Paid,");
        GeneratorTestHarness
            .InvokeStatic<string>(result, "Mapper", "Run")
            .ShouldBe("Pending|Paid|Enum value '42' of 'OrderStatusDto' cannot be mapped to 'OrderStatus'.");
    }

    [Theory]
    [InlineData("public enum Status { Pending, Refunded }", "public enum Target { Pending }", "'Refunded'")]
    [InlineData("public enum Status { Pending, Default = Pending }", "public enum Target { Pending, Default }", "share a value")]
    [InlineData("[System.Flags] public enum Status { A = 1, B = 2 }", "public enum Target { A = 1, B = 2 }", "flags")]
    public void RejectsEnumPairsTheSwitchCannotCover(string source, string target, string reason)
    {
        var result = GeneratorTestHarness.Generate(
            $$"""
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Wrapper Map(WrapperDto source);
            }

            {{source}}
            {{target}}
            public sealed record WrapperDto(Status Value);
            public sealed record Wrapper(Target Value);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR109" && x.GetMessage().Contains("Map") && x.GetMessage().Contains(reason));
        result.Errors.ShouldContain(x => x.Id == "DMPR101");
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void PrefersDomainFactoriesOverByNameEnumMapping()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static Target ToTarget(Status value) => value switch { Status.Legacy => Target.Archived, _ => Target.Active };

                public static partial Wrapper Map(WrapperDto source);

                public static string Run() => Map(new WrapperDto(Status.Legacy)).Value.ToString();
            }

            public enum Status { Active, Legacy }
            public enum Target { Active, Archived }
            public sealed record WrapperDto(Status Value);
            public sealed record Wrapper(Target Value);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("new global::Wrapper(ToTarget(source.Value))");
        result.Source.ShouldNotContain("switch");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("Archived");
    }

    [Fact]
    public void LiftsNullableEnumsAndSharesOneHelperPerPair()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MappingCompleteness(MappingCompleteness.Both)]
                public static partial Order Map(OrderDto source);

                public static string Run()
                {
                    var order = Map(new OrderDto(null, new List<Status> { Status.Open, Status.Closed }));
                    return $"{order.Current is null}|{string.Join(",", order.History)}";
                }
            }

            public enum Status { Open, Closed }
            public enum State { Open, Closed }
            public sealed record OrderDto(Status? Current, List<Status> History);
            public sealed record Order(State? Current, List<State> History);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("source.Current is null ? default(global::State?) : MapToState((source.Current).Value)");
        result.Source.Split("private static global::State MapToState(").Length.ShouldBe(2);
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("True|Open,Closed");
    }

    [Fact]
    public void RejectsEnumPairsInProjections()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Linq.Expressions;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Order Map(OrderDto source);

                [MapProjection(nameof(Map))]
                public static partial Expression<Func<OrderDto, Order>> Project();
            }

            public enum Status { Open }
            public enum State { Open }
            public sealed record OrderDto(Status Status);
            public sealed record Order(State Status);
            """,
            MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location)
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR106" && x.GetMessage().Contains("Status"));
        result.Source.ShouldContain("MapToState(source.Status)");
    }
}
