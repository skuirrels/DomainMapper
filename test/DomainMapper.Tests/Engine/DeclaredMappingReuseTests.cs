using DomainMapper.Projections;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Tests.Engine;

public sealed class DeclaredMappingReuseTests
{
    [Fact]
    public void ReusesDeclaredFactoryMappingsForNestedCollectionElements()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static OrderId ToOrderId(int value) => new(value);

                [MapToFactory(nameof(OrderLine.Create))]
                public static partial OrderLine ToLine(OrderLineDto source);

                [MapToFactory(nameof(Order.Place))]
                public static partial Order Place(OrderDto source);

                public static string Run()
                {
                    var placed = Place(new OrderDto(7, new List<OrderLineDto> { new("sku-1", 2), new("sku-2", 1) }));
                    var lines = string.Join(",", placed.Lines.Select(x => x.Sku + ":" + x.Quantity));
                    try
                    {
                        Place(new OrderDto(8, new List<OrderLineDto> { new("sku-3", 0) }));
                        return lines + "|accepted";
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return lines + "|rejected";
                    }
                }
            }

            public readonly record struct OrderId(int Value);
            public sealed record OrderLineDto(string Sku, int Quantity);
            public sealed record OrderDto(int Id, List<OrderLineDto> Lines);

            public sealed class OrderLine
            {
                private OrderLine(string sku, int quantity) { Sku = sku; Quantity = quantity; }
                public string Sku { get; }
                public int Quantity { get; }
                public static OrderLine Create(string sku, int quantity)
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
                    return new OrderLine(sku, quantity);
                }
            }

            public sealed class Order
            {
                private Order(OrderId id, IReadOnlyCollection<OrderLine> lines) { Id = id; Lines = lines; }
                public OrderId Id { get; }
                public IReadOnlyCollection<OrderLine> Lines { get; }
                public static Order Place(OrderId id, IReadOnlyCollection<OrderLine> lines) => new(id, lines);
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("target.Add(ToLine(item));");
        result.Source.ShouldNotContain("MapToOrderLine(");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("sku-1:2,sku-2:1|rejected");
    }

    [Fact]
    public void ReusesDeclaredMappingContractsForNestedMembers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Child.Label), nameof(ChildDto.Name))]
                [IgnoreSourceMember(nameof(ChildDto.Internal))]
                [MappingCompleteness(MappingCompleteness.Both)]
                public static partial Child MapChild(ChildDto source);

                public static partial Parent MapParent(ParentDto source);

                public static string Run() => MapParent(new ParentDto(1, new ChildDto("child", "secret"), null)).Child.Label
                    + "|" + (MapParent(new ParentDto(2, new ChildDto("x", "y"), null)).Optional is null ? "none" : "some");
            }

            public sealed record ChildDto(string Name, string Internal);
            public sealed record ParentDto(int Id, ChildDto Child, ChildDto? Optional);
            public sealed record Child(string Label);
            public sealed record Parent(int Id, Child Child, Child? Optional);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain(
            "new global::Parent(source.Id, MapChild(source.Child), source.Optional is null ? null : MapChild(source.Optional))"
        );
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("child|none");
    }

    [Fact]
    public void DoesNotReuseDeclaredMappingsForTheRootPair()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Target.Value), nameof(Source.First))]
                public static partial Target? MapFirst(Source? source);

                public static partial Target Plain(Source source);

                public static string Run() => MapFirst(new Source("first", "second"))!.Value + ":" + Plain(new Source("first", "second")).Value;
            }

            public sealed record Source(string First, string Value);
            public sealed record Target(string Value);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldNotContain("Plain(source");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("first:second");
    }

    [Fact]
    public void DoesNotReuseDeclaredMappingsInsideBoundedRecursion()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMaxDepth(2)]
                public static partial Tree Map(TreeSource source);

                public static partial Leaf MapLeaf(LeafSource source);
            }

            public sealed record LeafSource(int Value);
            public sealed record TreeSource(List<LeafSource> Leaves, TreeSource? Next);
            public sealed record Leaf(int Value);
            public sealed record Tree(List<Leaf> Leaves, Tree? Next);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("__depth");
        result.Source.ShouldNotContain("MapLeaf(item)");
        result.Source.ShouldContain("MapToLeaf(item");
    }

    [Fact]
    public void RejectsDeclaredMappingsThatReuseEachOtherInACycle()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Order MapOrder(OrderDto source);

                public static partial Customer MapCustomer(CustomerDto source);
            }

            public sealed record OrderDto(int Id, CustomerDto Customer);
            public sealed record CustomerDto(string Name, List<OrderDto> Orders);
            public sealed record Order(int Id, Customer Customer);
            public sealed record Customer(string Name, List<Order> Orders);
            """
        );

        result.Diagnostics.Count(x => x.Id == "DMPR102" && x.GetMessage().Contains("cycle")).ShouldBe(2);
        result.Source.ShouldNotContain("MapOrder(global::OrderDto source)");
        result.Source.ShouldNotContain("MapCustomer(global::CustomerDto source)");
    }

    [Fact]
    public void RejectsNestedValuesThatMatchMoreThanOneDeclaredMapping()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Child MapChildA(ChildDto source);

                public static partial Child MapChildB(ChildDto source);

                public static partial Parent MapParent(ParentDto source);
            }

            public sealed record ChildDto(string Name);
            public sealed record ParentDto(ChildDto Child);
            public sealed record Child(string Name);
            public sealed record Parent(Child Child);
            """
        );

        result.Diagnostics.ShouldContain(x =>
            x.Id == "DMPR102" && x.GetMessage().Contains("more than one declared mapping method") && x.GetMessage().Contains("MapParent")
        );
        result.Errors.ShouldContain(x => x.Id == "DMPR101");
        result.Source.ShouldContain("MapChildA(global::ChildDto source)");
        result.Source.ShouldNotContain("MapParent(global::ParentDto source)");
    }

    [Fact]
    public void RejectsProjectionsWhoseNestedValuesAreOwnedByConfiguredDeclaredMappings()
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
                [MapMember(nameof(Child.Label), nameof(ChildDto.Name))]
                public static partial Child MapChild(ChildDto source);

                public static partial Parent MapParent(ParentDto source);

                [MapProjection(nameof(MapParent))]
                public static partial Expression<Func<ParentDto, Parent>> Project();
            }

            public sealed record ChildDto(string Name);
            public sealed record ParentDto(int Id, ChildDto Child);
            public sealed record Child(string Label);
            public sealed record Parent(int Id, Child Child);
            """,
            MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location)
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR106" && x.GetMessage().Contains("Child"));
        result.Source.ShouldContain("MapChild(source.Child)");
    }
}
