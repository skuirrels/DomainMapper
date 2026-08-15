namespace DomainMapper.Tests.Engine;

public sealed class ExplicitMappingContractTests
{
    [Fact]
    public void GeneratesRenamedNestedComputedAndFieldMappingsWithoutReflection()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Target.EdcId), nameof(Source.ID))]
                [MapMember(nameof(Target.WarehouseDescription), nameof(Source.Warehouse) + "." + nameof(Warehouse.Description))]
                public static partial Target Map(Source source, string suffix);

                [MapTargetMember(nameof(Map), nameof(Target.Display))]
                private static string BuildDisplay(Source source, string suffix) => source.ID + suffix;
            }

            public sealed class Source
            {
                public int ID;
                public Warehouse? Warehouse { get; init; }
            }
            public sealed record Warehouse(string Description);
            public sealed class Target
            {
                public int EdcId;
                public string? WarehouseDescription { get; set; }
                public string Display { get; set; } = "";
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("target.EdcId = source.ID;");
        result.Source.ShouldContain("source.Warehouse?.Description");
        result.Source.ShouldContain("BuildDisplay(source, suffix)");
        result.Source.ShouldNotContain("System.Reflection");
    }

    [Fact]
    public void KeepsFieldsOutOfConventionMappingsUnlessExplicitlyConfigured()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed class Source
            {
                public int Field;
                public string Property { get; init; } = "";
            }

            public sealed class Target
            {
                public int Field;
                public string Property { get; set; } = "";
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("target.Property = source.Property;");
        result.Source.ShouldNotContain("target.Field = source.Field;");
    }

    [Fact]
    public void RequiresSourceFieldsToBeExplicitUnderSourceCompleteness()
    {
        var implicitResult = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MappingCompleteness(MappingCompleteness.Source)]
                public static partial Target Map(Source source);
            }

            public sealed class Source { public int Value; }
            public sealed class Target { public int Value { get; set; } }
            """
        );

        implicitResult.Diagnostics.ShouldContain(x => x.Id == "DMPR103" && x.GetMessage().Contains("Value"));

        var explicitResult = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Target.Value), nameof(Source.Value))]
                [MappingCompleteness(MappingCompleteness.Source)]
                public static partial Target Map(Source source);
            }

            public sealed class Source { public int Value; }
            public sealed class Target { public int Value { get; set; } }
            """
        );

        explicitResult.Errors.ShouldBeEmpty(explicitResult.Source);
        explicitResult.Source.ShouldContain("target.Value = source.Value;");
    }

    [Fact]
    public void EnforcesBothCompletenessWithTypedIgnores()
    {
        var valid = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MappingCompleteness(MappingCompleteness.Both)]
                [IgnoreSourceMember(nameof(Source.Transient))]
                [IgnoreTargetMember(nameof(Target.DatabaseId), Reason = "database generated")]
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id, string Transient);
            public sealed class Target
            {
                public int Id { get; set; }
                public long DatabaseId { get; set; }
            }
            """
        );

        valid.Errors.ShouldBeEmpty();
        valid.Source.ShouldContain("target.Id = source.Id;");
        valid.Source.ShouldNotContain("target.DatabaseId");

        var invalid = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MappingCompleteness(MappingCompleteness.Source)]
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id, string AddedLater);
            public sealed record Target(int Id);
            """
        );

        invalid.Diagnostics.ShouldContain(x => x.Id == "DMPR103" && x.GetMessage().Contains("AddedLater"));
    }

    [Fact]
    public void RejectsStaleOrAmbiguousConfiguration()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember("Removed", nameof(Source.Id))]
                public static partial Target Map(Source source);

                [MapCondition("MissingMap", nameof(Target.Id))]
                private static bool Stale(Source source) => true;
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """
        );

        result.Diagnostics.Count(x => x.Id == "DMPR102").ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void GeneratesAllowListedConditionalNullPreservingTrackedEntityUpdate()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapOnlyTargetMembers(nameof(Target.Name), nameof(Target.Amount))]
                [MapNull(nameof(Target.Name), NullMemberBehavior.PreserveTarget)]
                public static partial void Apply(Source source, Target target);

                [MapCondition(nameof(Apply), nameof(Target.Amount))]
                private static bool ShouldMapAmount(Source source, Target target) => source.ApplyAmount && target.Amount >= 0;

                public static string Run()
                {
                    var target = new Target { Identity = 42, Name = "kept", Amount = 10, Audit = "protected" };
                    Apply(new Source(null, 99, false), target);
                    return $"{target.Identity}|{target.Name}|{target.Amount}|{target.Audit}";
                }
            }

            public sealed record Source(string? Name, decimal Amount, bool ApplyAmount);
            public sealed class Target
            {
                public int Identity { get; set; }
                public string Name { get; set; } = "";
                public decimal Amount { get; set; }
                public string Audit { get; set; } = "";
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("if (source.Name is not null)");
        result.Source.ShouldContain("ShouldMapAmount(source, target)");
        result.Source.ShouldNotContain("target.Identity =");
        result.Source.ShouldNotContain("target.Audit =");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("42|kept|10|protected");
    }

    [Fact]
    public void AppliesDeclaredEmptyCollectionAndThrowNullPolicies()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapNull(nameof(Target.Items), NullMemberBehavior.EmptyCollection)]
                [MapNull(nameof(Target.Name), NullMemberBehavior.Throw)]
                [MapNullSubstitute(nameof(Target.Description), "unknown")]
                public static partial Target Map(Source source);

                public static string Run()
                {
                    var target = Map(new Source("ok", null, null));
                    return target.Name + ":" + target.Items.Count + ":" + target.Description;
                }
            }

            public sealed record Source(string? Name, List<int>? Items, string? Description);
            public sealed class Target
            {
                public string Name { get; set; } = "";
                public List<int> Items { get; set; } = [];
                public string Description { get; set; } = "";
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("throw new global::System.InvalidOperationException");
        result.Source.ShouldContain("source.Items is null ? new global::System.Collections.Generic.List<int>(0)");
        result.Source.ShouldContain("source.Description is null ? (string)(\"unknown\")");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("ok:0:unknown");
    }

    [Fact]
    public void SupportsTwoIndependentlyConfiguredDirections()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Api.CreatedDate), nameof(Domain.DateCreated))]
                public static partial Api ToApi(Domain source);

                [MapMember(nameof(Domain.DateCreated), nameof(Api.CreatedDate))]
                public static partial Domain ToDomain(Api source);
            }

            public sealed record Domain(long DateCreated);
            public sealed record Api(long CreatedDate);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("new global::Api(source.DateCreated)");
        result.Source.ShouldContain("new global::Domain(source.CreatedDate)");
    }

    [Fact]
    public void RejectsUnboundedRecursionAndGeneratesAnOptInDepthGuard()
    {
        var rejected = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Value, Source? Next);
            public sealed record Target(int Value, Target? Next);
            """
        );

        rejected.Diagnostics.ShouldContain(x => x.Id == "DMPR101");
        rejected.Source.ShouldBeEmpty();

        var bounded = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMaxDepth(3)]
                public static partial Target Map(Source source);

                public static int Run()
                {
                    var source = new Source(1, new Source(2, new Source(3, new Source(4, null))));
                    var target = Map(source);
                    return target.Next?.Next?.Next?.Value ?? -1;
                }
            }

            public sealed record Source(int Value, Source? Next);
            public sealed record Target(int Value, Target? Next);
            """
        );

        bounded.Errors.ShouldBeEmpty();
        bounded.Source.ShouldContain("if (__depth <= 0)");
        bounded.Source.ShouldContain("__depth - 1");
        GeneratorTestHarness.InvokeStatic<int>(bounded, "Mapper", "Run").ShouldBe(-1);
    }

    [Fact]
    public void InvokesTypedCompletionHooksAfterAssignmentsInDeclarationOrder()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source, string suffix);

                [MapAfter(nameof(Map))]
                private static void Complete(Source source, Target target, string suffix) => target.SetDisplay(source.Name + suffix);

                public static string Run() => Map(new Source("Ada"), "!").Display;
            }

            public sealed record Source(string Name);
            public sealed class Target
            {
                public string Name { get; set; } = "";

                [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
                public string Display { get; private set; } = "";

                public void SetDisplay(string value) => Display = value;
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result
            .Source.IndexOf("target.Name = source.Name", StringComparison.Ordinal)
            .ShouldBeLessThan(result.Source.IndexOf("Complete(source, target, suffix)", StringComparison.Ordinal));
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("Ada!");
    }

    [Fact]
    public void ReusesAndOverridesExplicitBindingsFromAnIncludedBaseMapping()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(BaseTarget.DisplayName), nameof(BaseSource.Name))]
                public static partial BaseTarget MapBase(BaseSource source);

                [IncludeMapping(nameof(MapBase))]
                [MapMember(nameof(DerivedTarget.DisplayName), nameof(DerivedSource.PreferredName))]
                public static partial DerivedTarget MapDerived(DerivedSource source);
            }

            public class BaseSource
            {
                public string Name { get; init; } = "";
            }
            public sealed class DerivedSource : BaseSource
            {
                public string PreferredName { get; init; } = "";
                public int Code { get; init; }
            }
            public class BaseTarget
            {
                public string DisplayName { get; set; } = "";
            }
            public sealed class DerivedTarget : BaseTarget
            {
                public int Code { get; set; }
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("target.DisplayName = source.PreferredName;");
        result.Source.ShouldContain("target.Code = source.Code;");
    }
}
