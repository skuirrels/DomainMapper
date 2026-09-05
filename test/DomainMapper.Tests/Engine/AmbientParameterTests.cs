namespace DomainMapper.Tests.Engine;

public sealed class AmbientParameterTests
{
    [Fact]
    public void BindsAdditionalParametersToRootConstructorParametersAndSettableMembers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Order Map(OrderDto source, DateTimeOffset placedAt, string channel);

                public static string Run()
                {
                    var order = Map(new OrderDto(7), new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), "web");
                    return $"{order.Id}|{order.PlacedAt:O}|{order.Channel}";
                }
            }

            public sealed record OrderDto(int Id);
            public sealed class Order
            {
                public Order(int id, DateTimeOffset placedAt) { Id = id; PlacedAt = placedAt; }
                public int Id { get; }
                public DateTimeOffset PlacedAt { get; }
                public string Channel { get; set; } = "";
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("new global::Order(source.Id, placedAt)");
        result.Source.ShouldContain("target.Channel = channel;");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("7|2026-01-02T03:04:05.0000000+00:00|web");
    }

    [Fact]
    public void PrefersAdditionalParametersOverSameNamedRootSourceMembers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Order Map(OrderDto source, string customerName);

                public static string Run() => Map(new OrderDto(1, "from-dto"), "from-parameter").CustomerName;
            }

            public sealed record OrderDto(int Id, string CustomerName);
            public sealed record Order(int Id, string CustomerName);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("new global::Order(source.Id, customerName)");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("from-parameter");

        var sourceCompleteness = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MappingCompleteness(MappingCompleteness.Both)]
                public static partial Order Map(OrderDto source, string customerName);
            }

            public sealed record OrderDto(int Id, string CustomerName);
            public sealed record Order(int Id, string CustomerName);
            """
        );

        sourceCompleteness.Diagnostics.ShouldContain(x => x.Id == "DMPR103" && x.GetMessage().Contains("CustomerName"));
    }

    [Fact]
    public void FillsNestedMembersFromAmbientParametersOnlyWhenTheNestedSourceLacksThem()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static TenantId ToTenantId(Guid value) => new(value);

                public static partial Order Map(OrderDto source, Guid tenantId);

                public static string Run()
                {
                    var ambient = Guid.Parse("11111111-1111-1111-1111-111111111111");
                    var own = Guid.Parse("22222222-2222-2222-2222-222222222222");
                    var order = Map(new OrderDto(new List<LineDto> { new("sku") }, new NoteDto("n", own)), ambient);
                    return $"{order.Lines[0].TenantId.Value}|{order.Note.TenantId.Value}";
                }
            }

            public readonly record struct TenantId(Guid Value);
            public sealed record LineDto(string Sku);
            public sealed record NoteDto(string Text, Guid TenantId);
            public sealed record OrderDto(List<LineDto> Lines, NoteDto Note);
            public sealed record Line(string Sku, TenantId TenantId);
            public sealed record Note(string Text, TenantId TenantId);
            public sealed record Order(List<Line> Lines, Note Note);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("new global::Line(source.Sku, ToTenantId(__ambient0))");
        result.Source.ShouldContain("new global::Note(source.Text, ToTenantId(source.TenantId))");
        GeneratorTestHarness
            .InvokeStatic<string>(result, "Mapper", "Run")
            .ShouldBe("11111111-1111-1111-1111-111111111111|22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public void RejectsNonWritableRootStateMatchedOnlyByAnAdditionalParameter()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Order Map(OrderDto source, DateTimeOffset placedAt);
            }

            public sealed record OrderDto(int Id);
            public sealed class Order
            {
                public Order(int id) => Id = id;
                public int Id { get; }
                public DateTimeOffset PlacedAt { get; }
            }
            """
        );

        result.Errors.ShouldContain(x => x.Id == "DMPR101");
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void IgnoresAdditionalParametersThatMatchNoTargetMember()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source, string timeZone);

                [MapTargetMember(nameof(Map), nameof(Target.Display))]
                private static string Display(Source source, string timeZone) => source.Name + "@" + timeZone;

                public static string Run() => Map(new Source("a"), "UTC").Display;
            }

            public sealed record Source(string Name);
            public sealed record Target(string Name, string Display);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("a@UTC");
    }
}
