using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DomainMapper.Projections;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Tests.Engine;

/// <summary>Full-file snapshots of representative generated mappers. Accept intentional changes by updating the verified files.</summary>
public sealed class GeneratedSourceSnapshotTests
{
    private static readonly Regex GeneratedCodeStamp = new("GeneratedCode\\(\"DomainMapper\", \"[^\"]+\"\\)");

    [Fact]
    public Task ConventionCreateWithConstructorAssignmentsAndCollections() =>
        VerifySource(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id, string Name, string? Note, List<ChildSource> Children, Dictionary<string, int> Counts);
            public sealed record ChildSource(int Value);
            public sealed class Target
            {
                public Target(int id) => Id = id;
                public int Id { get; }
                public string Name { get; set; } = "";
                public string? Note { get; set; }
                public IReadOnlyList<ChildTarget> Children { get; set; } = [];
                public IReadOnlyDictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
            }
            public sealed record ChildTarget(int Value);
            """
        );

    [Fact]
    public Task TargetFactoryWithDomainFactoryAndAdditionalParameter() =>
        VerifySource(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static OrderId ToOrderId(int value) => new(value);

                [MapToFactory(nameof(Order.Place))]
                public static partial Order Place(OrderDraft source, string customerName);
            }

            public sealed record OrderDraft(int Id, decimal Total);
            public readonly record struct OrderId(int Value);
            public sealed record Order(OrderId Id, string CustomerName, decimal Total)
            {
                public static Order Place(OrderId id, string customerName, decimal total) => new(id, customerName, total);
            }
            """
        );

    [Fact]
    public Task ReferenceTrackedGraphWithRuntimeRegistry() =>
        VerifySource(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                public static partial Node Map(NodeSource source);
            }

            public sealed class NodeSource
            {
                public int Value { get; set; }
                public NodeSource? Next { get; set; }
                public List<NodeSource> Children { get; set; } = new();
            }

            public sealed class Node
            {
                public int Value { get; set; }
                public Node? Next { get; set; }
                public List<Node> Children { get; set; } = new();
            }
            """
        );

    [Fact]
    public Task ExistingTargetUpdateWithCollectionNullAndCompletionPolicies() =>
        VerifySource(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapCollection(nameof(Target.Tags), CollectionUpdatePolicy.ClearAndFill)]
                [MapNull(nameof(Target.Name), NullMemberBehavior.PreserveTarget)]
                [MapOnlyTargetMembers(nameof(Target.Name), nameof(Target.Tags), nameof(Target.Count))]
                public static partial void Update(Source source, Target target);

                [MapAfter(nameof(Update))]
                private static void Touch(Target target) => target.Touched = true;
            }

            public sealed record Source(string? Name, List<string> Tags, int Count, string Ignored);
            public sealed class Target
            {
                public string Name { get; set; } = "";
                public List<string> Tags { get; } = new();
                public int Count { get; set; }
                public bool Touched { get; set; }
            }
            """
        );

    [Fact]
    public Task CachedProjectionWithNestedNullableMember() =>
        VerifySource(
            """
            using System;
            using System.Linq.Expressions;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Target.ExternalId), nameof(Source.Id))]
                public static partial Target Map(Source source);

                [MapProjection(nameof(Map))]
                public static partial Expression<Func<Source, Target>> Project();
            }

            public sealed record Source(int Id, string Name, Address? Address);
            public sealed record Address(string City);
            public sealed record Target(int ExternalId, string Name, TargetAddress? Address);
            public sealed record TargetAddress(string City);
            """,
            MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location)
        );

    private static Task VerifySource(string source, MetadataReference? additionalReference = null, [CallerMemberName] string testName = "")
    {
        var result = GeneratorTestHarness.Generate(source, additionalReference == null ? [] : [additionalReference]);
        result.Errors.ShouldBeEmpty(result.Source);

        var settings = new VerifySettings();
        settings.UseDirectory("_snapshots");
        settings.UseMethodName(testName);
        settings.ScrubLinesWithReplace(line => GeneratedCodeStamp.Replace(line, "GeneratedCode(\"DomainMapper\", \"{version}\")"));
        return Verifier.Verify(result.Source, settings);
    }
}
