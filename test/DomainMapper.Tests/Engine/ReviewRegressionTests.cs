namespace DomainMapper.Tests.Engine;

public sealed class ReviewRegressionTests
{
    [Fact]
    public void KeepsConfiguredHelpersIsolatedPerMappingMethod()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Target.Value), nameof(Source.First))]
                public static partial Target? MapFirst(Source? source);

                [MapMember(nameof(Target.Value), nameof(Source.Second))]
                public static partial Target? MapSecond(Source? source);

                public static string Run()
                {
                    var source = new Source("first", "second");
                    return MapFirst(source)!.Value + ":" + MapSecond(source)!.Value;
                }
            }

            public sealed record Source(string First, string Second);
            public sealed record Target(string Value);
            """
        );

        result.Errors.ShouldBeEmpty();
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("first:second");
    }

    [Theory]
    [InlineData("[MappingCompleteness((MappingCompleteness)99)]")]
    [InlineData("[MapNull(nameof(Target.Value), (NullMemberBehavior)99)]")]
    [InlineData("[MapMaxDepth(2, ExhaustionBehavior = (DepthExhaustionBehavior)99)]")]
    public void RejectsUndefinedPolicyEnumValues(string attribute)
    {
        var result = GeneratorTestHarness.Generate(
            $$"""
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                {{attribute}}
                public static partial Target Map(Source source);
            }

            public sealed record Source(string Value);
            public sealed record Target(string Value);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR102");
    }

    [Fact]
    public void RejectsNullPoliciesThatCanNeverObserveNull()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapNull(nameof(Target.Value), NullMemberBehavior.Throw)]
                public static partial Target Map(Source source);
            }

            public sealed record Source(string Value);
            public sealed record Target(string Value);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR102" && x.GetMessage().Contains("null", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("[MapNull(nameof(Target.Value), NullMemberBehavior.Assign)]")]
    [InlineData("[MapNull(nameof(Target.Value), NullMemberBehavior.EmptyCollection)]")]
    public void RejectsNullPoliciesThatAreIncompatibleWithTheTarget(string attribute)
    {
        var result = GeneratorTestHarness.Generate(
            $$"""
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                {{attribute}}
                public static partial Target Map(Source source);
            }

            public sealed record Source(string? Value);
            public sealed record Target(string Value);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR102");
    }

    [Fact]
    public void EmitsValidEnumNullSubstitutes()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapNullSubstitute(nameof(Target.Status), Status.Unknown)]
                public static partial Target Map(Source source);

                public static int Run() => (int)Map(new Source(null)).Status;
            }

            public enum Status { Unknown, Ready }
            public sealed record Source(Status? Status);
            public sealed record Target(Status Status);
            """
        );

        result.Errors.ShouldBeEmpty();
        GeneratorTestHarness.InvokeStatic<int>(result, "Mapper", "Run").ShouldBe(0);
    }

    [Fact]
    public void DeferredCreationHandlesMarkerLikeConstantText()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapNullSubstitute(nameof(Target.Description), ")__|fallback")]
                public static partial Target Map(Source source);

                public static string Run() => Map(new Source(42, null)).Description;
            }

            public sealed record Source(int Id, string? Description);
            public sealed class Target
            {
                public int Id { get; set; }
                public string Description { get; set; } = "";
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe(")__|fallback");
    }

    [Fact]
    public void IncludedMappingChainsRetainTheNearestOverride()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Target.Value), nameof(Source.First))]
                public static partial Target MapBase(Source source);

                [IncludeMapping(nameof(MapBase))]
                [MapMember(nameof(Target.Value), nameof(Source.Second))]
                public static partial Target MapIntermediate(Source source);

                [IncludeMapping(nameof(MapIntermediate))]
                public static partial Target MapDerived(Source source);

                public static string Run() => MapDerived(new Source("first", "second")).Value;
            }

            public sealed record Source(string First, string Second);
            public sealed record Target(string Value);
            """
        );

        result.Errors.ShouldBeEmpty();
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("second");
    }

    [Fact]
    public void RejectsConfigurationForInaccessibleTargetState()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember("Secret", nameof(Source.Secret))]
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id, int Secret);
            public sealed class Target
            {
                public int Id { get; set; }
                private int Secret { get; set; }
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR102" && x.GetMessage().Contains("Secret"));
    }

    [Fact]
    public void RejectsHelperConfigurationForOverloadedMappingNames()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(SourceA source);
                public static partial Target Map(SourceB source);

                [MapTargetMember(nameof(Map), nameof(Target.Value))]
                private static string Value(SourceA source) => source.Value;
            }

            public sealed record SourceA(string Value);
            public sealed record SourceB(string Value);
            public sealed record Target(string Value);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR102" && x.GetMessage().Contains("overload", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceCompletenessUsesOnlyTheSelectedFactoryOverload()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapToFactory(nameof(Target.Create))]
                [MappingCompleteness(MappingCompleteness.Source)]
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id, string Extra);
            public sealed record Target(int Id)
            {
                public static Target Create(int id) => new(id);
                public static Target Create(DateTime extra) => new(0);
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR103" && x.GetMessage().Contains("Extra"));
    }

    [Fact]
    public void SourceCompletenessDoesNotTreatExplicitFactoryArgumentsAsSourceConsumption()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapToFactory(nameof(Target.Create))]
                [MappingCompleteness(MappingCompleteness.Source)]
                public static partial Target Map(Source source, string name);
            }

            public sealed record Source(int Id, string Name);
            public sealed record Target(int Id, string Name)
            {
                public static Target Create(int id, string name) => new(id, name);
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR103" && x.GetMessage().Contains("Name"));
    }

    [Fact]
    public void ExplicitFactoryArgumentsDoNotActivateUnusedMemberBindings()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapToFactory(nameof(Target.Create))]
                [MapMember(nameof(Target.Id), nameof(Source.Id))]
                [MappingCompleteness(MappingCompleteness.Source)]
                public static partial Target Map(Source source, int id);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id)
            {
                public static Target Create(int id) => new(id);
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR103" && x.GetMessage().Contains("Id"));
    }

    [Fact]
    public void SourceCompletenessRequiresACompatibleConventionMapping()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MappingCompleteness(MappingCompleteness.Source)]
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id, string Extra);
            public sealed class Target
            {
                public int Id { get; set; }
                public int Extra { get; set; }
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR103" && x.GetMessage().Contains("Extra"));
    }

    [Fact]
    public void RejectsNonNullableHelperParametersForNullPropagatingPaths()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Target.Value), "Child.Value")]
                public static partial Target Map(Source source);

                [MapTargetMember(nameof(Map), nameof(Target.Value))]
                private static string Compute(string value) => value;
            }

            public sealed record Source(Child? Child);
            public sealed record Child(string Value);
            public sealed record Target(string Value);
            """
        );

        result.Diagnostics.ShouldContain(x =>
            x.Id == "DMPR102" && x.GetMessage().Contains("parameter contract", StringComparison.OrdinalIgnoreCase)
        );
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void NullableRootsSupportNonNullableComputedAndCompletionHelpers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [IgnoreTargetMember(nameof(Target.Completed), Reason = "set by completion hook")]
                public static partial Target? Map(Source? source);

                [MapTargetMember(nameof(Map), nameof(Target.Display))]
                private static string Compute(Source source) => source.Name.ToUpperInvariant();

                [MapAfter(nameof(Map))]
                private static void Complete(Source source, Target target) => target.Completed = true;

                public static string Run()
                {
                    var mapped = Map(new Source("Ada"));
                    return mapped!.Display + ":" + mapped.Completed + ":" + (Map(null) is null);
                }
            }

            public sealed record Source(string Name);
            public sealed class Target
            {
                public string Name { get; set; } = "";
                public string Display { get; set; } = "";
                public bool Completed { get; set; }
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Warnings.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("ADA:True:True");
    }

    [Theory]
    [InlineData("Source? source, Target target")]
    [InlineData("Source source, Target? target")]
    public void RejectsNullableExistingTargetContracts(string parameters)
    {
        var result = GeneratorTestHarness.Generate(
            $$"""
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial void Apply({{parameters}});
            }

            public sealed record Source(string Value);
            public sealed class Target { public string Value { get; set; } = ""; }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR100");
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void RejectsNullEntriesInExistingTargetAllowLists()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapOnlyTargetMembers(nameof(Target.Value), null!)]
                public static partial void Apply(Source source, Target target);
            }

            public sealed record Source(string Value);
            public sealed class Target { public string Value { get; set; } = ""; }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR102");
    }

    [Theory]
    [InlineData(
        "System.Guid",
        """
            public readonly struct Wrapper
            {
                private Wrapper(System.Guid value) => Value = value;
                public System.Guid Value { get; }
                public static Wrapper From(System.Guid value) => new(value);
            }
            """
    )]
    [InlineData(
        "int",
        """
            public readonly struct Wrapper
            {
                public readonly int Value;
                private Wrapper(int value) => Value = value;
                public static Wrapper From(int value) => new(value);
            }
            """
    )]
    [InlineData(
        "string",
        """
            public sealed class Wrapper
            {
                public Wrapper() { }
                public Wrapper(string value, bool marker) => Value = value;
                public string Value { get; } = "";
            }
            """
    )]
    public void RejectsParameterlessConstructionThatConsumesNoSourceData(string sourceType, string wrapper)
    {
        var result = GeneratorTestHarness.Generate(
            $$"""
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source({{sourceType}} Id, string Name);
            public sealed record Target(Wrapper Id, string Name);
            {{wrapper}}
            """
        );

        result.Errors.ShouldContain(x => x.Id == "DMPR101");
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void RejectsParameterlessStructConstructionUnderSourceCompleteness()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MappingCompleteness(MappingCompleteness.Both)]
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed record Target(Wrapper Id);
            public readonly record struct Wrapper
            {
                private Wrapper(int value) => Value = value;
                public int Value { get; }
                public static Wrapper From(int value) => new(value);
            }
            """
        );

        result.Errors.ShouldContain(x => x.Id == "DMPR101");
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void KeepsStatelessTargetsConstructibleFromParameterlessConstructors()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);

                public static string Run() => Map(new Source(new MarkerSource(), "name")).Marker is Marker ? "ok" : "missing";
            }

            public sealed class MarkerSource;
            public sealed class Marker;
            public sealed record Source(MarkerSource Marker, string Name);
            public sealed record Target(Marker Marker, string Name);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("new global::Marker()");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("ok");
    }
}
