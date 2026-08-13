using Microsoft.CodeAnalysis;

namespace DomainMapper.Tests.Engine;

public sealed class GeneratorContractTests
{
    [Fact]
    public void GeneratesDirectPropertyAssignments()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class ContractMapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id, string Name);
            public sealed class Target
            {
                public int Id { get; set; }
                public string Name { get; set; } = string.Empty;
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("target.Id = source.Id;");
        result.Source.ShouldContain("target.Name = source.Name;");
        result.Source.ShouldNotContain("System.Reflection");
    }

    [Fact]
    public void DoesNotForceJitInliningPolicyOntoGeneratedMappings()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class ContractMapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldNotContain("MethodImpl");
        result.Source.ShouldNotContain("AggressiveInlining");
    }

    [Fact]
    public void UsesIndexedListMappingWithoutAdditionalRuntimeAllocation()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class ContractMapper
            {
                public static partial List<Target> Map(List<Source> source);
            }

            public sealed record Source(int Value);
            public sealed record Target(int Value);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("new global::System.Collections.Generic.List<global::Target>(source.Count)");
        result.Source.ShouldContain("for (var i = 0; i < source.Count; i++)");
        result.Source.ShouldNotContain(".Select(");
    }

    [Fact]
    public void RoutesAggregateConstructionThroughTargetFactory()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class ContractMapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static OrderId ToOrderId(int value) => new(value);

                [MapToFactory(nameof(Order.Place))]
                public static partial Order Place(OrderDraft source);
            }

            public sealed record OrderDraft(int Id, string CustomerName);
            public readonly record struct OrderId(int Value);
            public sealed record Order(OrderId Id, string CustomerName)
            {
                public static Order Place(OrderId id, string customerName) => new(id, customerName);
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("global::Order.Place(ToOrderId(source.Id), source.CustomerName)");
    }

    [Fact]
    public void RejectsMethodsOutsideTheMappingContract()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class ContractMapper
            {
                public static partial void Map();
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR100" && x.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RejectsTargetsWithoutAnAccessibleConstructionPath()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class ContractMapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed class Target
            {
                private Target(int id) => Id = id;
                public int Id { get; }
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR101" && x.Severity == DiagnosticSeverity.Error);
    }
}
