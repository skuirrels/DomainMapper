using DomainMapper.Projections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Tests.Engine;

public sealed class AdvancedProductContractTests
{
    [Fact]
    public void KeepsUnrelatedMapperOutputCachedAfterAnIsolatedContractEdit()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var mapperA = CSharpSyntaxTree.ParseText(
            """
            using DomainMapper.Abstractions;
            [DomainMapper] public static partial class MapperA { public static partial TargetA Map(SourceA source); }
            public sealed record SourceA(int Value);
            public sealed record TargetA(int Value);
            """,
            parseOptions,
            "MapperA.cs"
        );
        var mapperB = CSharpSyntaxTree.ParseText(
            """
            using DomainMapper.Abstractions;
            [DomainMapper] public static partial class MapperB { public static partial TargetB Map(SourceB source); }
            public sealed record SourceB(int Value);
            public sealed record TargetB(int Value);
            """,
            parseOptions,
            "MapperB.cs"
        );
        var compilation = GeneratorTestHarness.CreateCompilation([mapperA, mapperB]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DomainMapperGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true),
            parseOptions: parseOptions
        );
        driver = driver.RunGenerators(compilation);

        var editedA = CSharpSyntaxTree.ParseText(
            mapperA.GetText().ToString().Replace("int Value", "long Value"),
            parseOptions,
            "MapperA.cs"
        );
        compilation = compilation.ReplaceSyntaxTree(mapperA, editedA);
        driver = driver.RunGenerators(compilation);

        var reasons = driver
            .GetRunResult()
            .Results.Single()
            .TrackedSteps["MapperContracts"]
            .SelectMany(x => x.Outputs)
            .Select(x => x.Reason)
            .ToArray();
        reasons.ShouldContain(IncrementalStepRunReason.Modified);
        reasons.ShouldContain(x => x == IncrementalStepRunReason.Cached || x == IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void RegeneratesEachMapperAffectedByASharedContractEdit()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var contracts = CSharpSyntaxTree.ParseText(
            "public sealed record Source(int Value); public sealed record Target(int Value);",
            parseOptions,
            "Contracts.cs"
        );
        var mapperA = CSharpSyntaxTree.ParseText(
            "using DomainMapper.Abstractions; [DomainMapper] public static partial class MapperA { public static partial Target Map(Source source); }",
            parseOptions,
            "MapperA.cs"
        );
        var mapperB = CSharpSyntaxTree.ParseText(
            "using DomainMapper.Abstractions; [DomainMapper] public static partial class MapperB { public static partial Target Map(Source source); }",
            parseOptions,
            "MapperB.cs"
        );
        var compilation = GeneratorTestHarness.CreateCompilation([contracts, mapperA, mapperB]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DomainMapperGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true),
            parseOptions: parseOptions
        );
        driver = driver.RunGenerators(compilation);

        var editedContracts = CSharpSyntaxTree.ParseText(
            contracts.GetText().ToString().Replace("int Value", "long Value"),
            parseOptions,
            "Contracts.cs"
        );
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(contracts, editedContracts));

        driver
            .GetRunResult()
            .Results.Single()
            .TrackedSteps["MapperContracts"]
            .SelectMany(x => x.Outputs)
            .Select(x => x.Reason)
            .ShouldAllBe(x => x == IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void RegeneratesWhenAReferencedContractAssemblyIdentityChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var mapper = CSharpSyntaxTree.ParseText(
            "using Contracts; using DomainMapper.Abstractions; [DomainMapper] public static partial class Mapper { public static partial Target Map(Source source); }",
            parseOptions,
            "Mapper.cs"
        );
        var contractsV1 = GeneratorTestHarness.CompileReference(
            "Contracts",
            """
            using System.Reflection;
            [assembly: AssemblyVersion("1.0.0.0")]
            namespace Contracts;
            public sealed class Source { public int Value { get; set; } }
            public sealed class Target { public int Value { get; set; } }
            """
        );
        var contractsV2 = GeneratorTestHarness.CompileReference(
            "Contracts",
            """
            using System.Reflection;
            [assembly: AssemblyVersion("2.0.0.0")]
            namespace Contracts;
            public sealed class Source { public long Value { get; set; } }
            public sealed class Target { public long Value { get; set; } }
            """
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DomainMapperGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true),
            parseOptions: parseOptions
        );
        driver = driver.RunGenerators(GeneratorTestHarness.CreateCompilation([mapper], contractsV1));
        driver = driver.RunGenerators(GeneratorTestHarness.CreateCompilation([mapper], contractsV2));

        driver
            .GetRunResult()
            .Results.Single()
            .TrackedSteps["MapperContracts"]
            .SelectMany(x => x.Outputs)
            .ShouldHaveSingleItem()
            .Reason.ShouldBe(IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void RegeneratesWhenAContainingPartialTypeContractChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var container = CSharpSyntaxTree.ParseText("public partial class Container<T> { }", parseOptions, "Container.cs");
        var mapper = CSharpSyntaxTree.ParseText(
            "using DomainMapper.Abstractions; public partial class Container<T> { [DomainMapper] public static partial class Mapper { public static partial Target Map(Source source); } } public sealed record Source(int Value); public sealed record Target(int Value);",
            parseOptions,
            "Mapper.cs"
        );
        var compilation = GeneratorTestHarness.CreateCompilation([container, mapper]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DomainMapperGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true),
            parseOptions: parseOptions
        );
        driver = driver.RunGenerators(compilation);

        var editedContainer = CSharpSyntaxTree.ParseText(
            "public partial class Container<T> where T : class { }",
            parseOptions,
            "Container.cs"
        );
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(container, editedContainer));

        driver
            .GetRunResult()
            .Results.Single()
            .TrackedSteps["MapperContracts"]
            .SelectMany(x => x.Outputs)
            .ShouldHaveSingleItem()
            .Reason.ShouldBe(IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void RegeneratesWhenAnInheritedMapperMemberChangesHelperNaming()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var baseMapper = CSharpSyntaxTree.ParseText(
            "public class MapperBase { protected static void Available() { } }",
            parseOptions,
            "MapperBase.cs"
        );
        var mapper = CSharpSyntaxTree.ParseText(
            "using DomainMapper.Abstractions; [DomainMapper] public partial class Mapper : MapperBase { public static partial Target Map(Source source); } public sealed record Source(ChildSource Child); public sealed record Target(ChildTarget Child); public sealed record ChildSource(int Value); public sealed record ChildTarget(int Value);",
            parseOptions,
            "Mapper.cs"
        );
        var compilation = GeneratorTestHarness.CreateCompilation([baseMapper, mapper]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DomainMapperGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true),
            parseOptions: parseOptions
        );
        driver = driver.RunGenerators(compilation);

        var editedBaseMapper = CSharpSyntaxTree.ParseText(
            "public class MapperBase { protected static void MapToChildTarget() { } }",
            parseOptions,
            "MapperBase.cs"
        );
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(baseMapper, editedBaseMapper));
        var result = driver.GetRunResult();

        result
            .Results.Single()
            .TrackedSteps["MapperContracts"]
            .SelectMany(x => x.Outputs)
            .ShouldHaveSingleItem()
            .Reason.ShouldBe(IncrementalStepRunReason.Modified);
        result.GeneratedTrees.Single().GetText().ToString().ShouldContain("MapToChildTarget2");
    }

    [Fact]
    public void AppliesExplicitClearAndFillAndAppendCollectionPolicies()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapOnlyTargetMembers(nameof(Target.Items))]
                [MapCollection(nameof(Target.Items), CollectionUpdatePolicy.ClearAndFill)]
                public static partial void ReplaceItems(Source source, Target target);

                [MapOnlyTargetMembers(nameof(Target.Items))]
                [MapCollection(nameof(Target.Items), CollectionUpdatePolicy.Append)]
                public static partial void AppendItems(Source source, Target target);

                public static string Run()
                {
                    var target = new Target();
                    target.Items.Add(9);
                    ReplaceItems(new Source([1, 2]), target);
                    AppendItems(new Source([2, 3]), target);
                    return string.Join(",", target.Items);
                }
            }

            public sealed record Source(List<int> Items);
            public sealed class Target { public List<int> Items { get; } = []; }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("__collection_Items.Clear();");
        result.Source.ShouldContain("__collection_Items.Add(item);");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("1,2,2,3");
    }

    [Fact]
    public void AppliesDocumentedNullCollectionMutationBehavior()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;
            [DomainMapper]
            public static partial class Mapper
            {
                [MapOnlyTargetMembers(nameof(Target.Cleared))]
                [MapCollection(nameof(Target.Cleared), CollectionUpdatePolicy.ClearAndFill)]
                [MapMember(nameof(Target.Cleared), nameof(Source.Items))]
                public static partial void Clear(Source source, Target target);

                [MapOnlyTargetMembers(nameof(Target.Appended))]
                [MapCollection(nameof(Target.Appended), CollectionUpdatePolicy.Append)]
                [MapMember(nameof(Target.Appended), nameof(Source.Items))]
                public static partial void Append(Source source, Target target);

                public static string Run()
                {
                    var target = new Target();
                    Clear(new Source(null), target);
                    Append(new Source(null), target);
                    return $"{target.Cleared.Count}|{string.Join(",", target.Appended)}";
                }
            }
            public sealed record Source(List<int>? Items);
            public sealed class Target
            {
                public List<int> Cleared { get; } = [9];
                public List<int> Appended { get; } = [8];
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("0|8");
    }

    [Fact]
    public void EvaluatesNullableCollectionSourcesOncePerMutation()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;
            [DomainMapper]
            public static partial class Mapper
            {
                [MapOnlyTargetMembers(nameof(Target.Items))]
                [MapCollection(nameof(Target.Items), CollectionUpdatePolicy.ClearAndFill)]
                public static partial void Apply(Source source, Target target);

                public static int Run()
                {
                    var source = new Source();
                    Apply(source, new Target());
                    return source.ReadCount;
                }
            }
            public sealed class Source
            {
                public int ReadCount { get; private set; }
                public List<int>? Items
                {
                    get
                    {
                        ReadCount++;
                        return [1, 2];
                    }
                }
            }
            public sealed class Target { public List<int> Items { get; } = []; }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<int>(result, "Mapper", "Run").ShouldBe(1);
    }

    [Fact]
    public void RejectsReplaceForAReadOnlyCollectionMember()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;
            [DomainMapper]
            public static partial class Mapper
            {
                [MapCollection(nameof(Target.Items), CollectionUpdatePolicy.Replace)]
                public static partial void Apply(Source source, Target target);
            }
            public sealed record Source(List<int> Items);
            public sealed class Target { public List<int> Items { get; } = []; }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR102" && x.GetMessage().Contains("requires a writable target"));
    }

    [Fact]
    public void PreservesSelfCyclesAndSharedReferencesPerInvocation()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                public static partial Target Map(Source source);

                public static bool Run()
                {
                    var shared = new Source { Value = 2 };
                    var root = new Source { Value = 1, Left = shared, Right = shared };
                    root.Next = root;
                    var target = Map(root);
                    var second = Map(root);
                    return ReferenceEquals(target, target.Next)
                        && ReferenceEquals(target.Left, target.Right)
                        && !ReferenceEquals(target, second);
                }
            }

            public sealed class Source
            {
                public int Value { get; set; }
                public Source? Next { get; set; }
                public Source? Left { get; set; }
                public Source? Right { get; set; }
            }
            public sealed class Target
            {
                public int Value { get; set; }
                public Target? Next { get; set; }
                public Target? Left { get; set; }
                public Target? Right { get; set; }
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("__DomainMapperReferenceKey");
        result.Source.ShouldContain("RuntimeHelpers.GetHashCode(_source)");
        result.Source.ShouldContain("__references.TryGetValue");
        GeneratorTestHarness.InvokeStatic<bool>(result, "Mapper", "Run").ShouldBeTrue();
    }

    [Fact]
    public void TracksOneSourceReferenceIndependentlyForEachTargetType()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                [MapMember(nameof(Target.First), nameof(Source.Shared))]
                [MapMember(nameof(Target.Again), nameof(Source.Shared))]
                [MapMember(nameof(Target.Second), nameof(Source.Shared))]
                public static partial Target Map(Source source);

                public static bool Run()
                {
                    var target = Map(new Source { Shared = new SharedSource { Value = 42 } });
                    return target.First?.Value == 42
                        && ReferenceEquals(target.First, target.Again)
                        && target.Second?.Value == 42;
                }
            }

            public sealed class Source { public SharedSource Shared { get; set; } = new(); }
            public sealed class SharedSource { public int Value { get; set; } }
            public sealed class Target
            {
                public FirstTarget? First { get; set; }
                public FirstTarget? Again { get; set; }
                public SecondTarget? Second { get; set; }
            }
            public sealed class FirstTarget { public int Value { get; set; } }
            public sealed class SecondTarget { public int Value { get; set; } }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<bool>(result, "Mapper", "Run").ShouldBeTrue();
    }

    [Fact]
    public void PreservesParentChildCyclesThroughCollections()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;
            [DomainMapper]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                public static partial Target Map(Source source);
                public static bool Run()
                {
                    var parent = new Source { Value = 1 };
                    var child = new Source { Value = 2, Parent = parent };
                    parent.Children.Add(child);
                    var target = Map(parent);
                    return ReferenceEquals(target, target.Children[0].Parent);
                }
            }
            public sealed class Source
            {
                public int Value { get; set; }
                public Source? Parent { get; set; }
                public List<Source> Children { get; set; } = [];
            }
            public sealed class Target
            {
                public int Value { get; set; }
                public Target? Parent { get; set; }
                public List<Target> Children { get; set; } = [];
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<bool>(result, "Mapper", "Run").ShouldBeTrue();
    }

    [Fact]
    public void MapsDeepAcyclicAndNullableTrackedNodesDeterministically()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                public static partial Target Map(Source source);
                public static bool Run()
                {
                    var root = new Source { Value = 0 };
                    var current = root;
                    for (var value = 1; value <= 128; value++)
                    {
                        current.Next = new Source { Value = value };
                        current = current.Next;
                    }
                    var target = Map(root);
                    var count = 0;
                    for (var node = target; node != null; node = node.Next)
                        count++;
                    return count == 129 && current.Next is null;
                }
            }
            public sealed class Source { public int Value { get; set; } public Source? Next { get; set; } }
            public sealed class Target { public int Value { get; set; } public Target? Next { get; set; } }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<bool>(result, "Mapper", "Run").ShouldBeTrue();
    }

    [Fact]
    public void KeepsOrdinaryMappingsTrackerFreeAndRejectsUnsupportedNestedTrackingShapes()
    {
        var ordinary = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper] public static partial class Mapper { public static partial Target Map(Source source); }
            public sealed record Source(int Value);
            public sealed record Target(int Value);
            """
        );
        ordinary.Errors.ShouldBeEmpty(ordinary.Source);
        ordinary.Source.ShouldNotContain("__references");
        ordinary.Source.ShouldNotContain("__DomainMapperReferenceKey");

        var unsupported = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;
            [DomainMapper]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                public static partial Target Map(Source source);
            }
            public sealed class Source { public IEnumerable<Source> Items { get; set; } = []; }
            public sealed class Target { public Target[] Items { get; set; } = []; }
            """
        );
        unsupported.Errors.ShouldHaveSingleItem().Id.ShouldBe("DMPR105");
    }

    [Fact]
    public void ResolvesTrackedReferencesBeforeApplyingDepthToNewObjects()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                [MapMaxDepth(1)]
                public static partial Target Map(Source source);

                public static bool Run()
                {
                    var root = new Source();
                    root.Self = root;
                    root.Next = new Source();
                    var target = Map(root);
                    return ReferenceEquals(target, target.Self) && target.Next is null;
                }
            }
            public sealed class Source { public Source? Self { get; set; } public Source? Next { get; set; } }
            public sealed class Target { public Target? Self { get; set; } public Target? Next { get; set; } }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<bool>(result, "Mapper", "Run").ShouldBeTrue();
    }

    [Fact]
    public void KeepsGeneratedInfrastructureSafeAcrossConcurrentInvocations()
    {
        var projectionReference = MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location);
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Linq.Expressions;
            using System.Threading;
            using System.Threading.Tasks;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                public static partial Target Map(Source source);
                public static partial PlainTarget MapPlain(PlainSource source);
                [MapProjection(nameof(MapPlain))]
                public static partial Expression<Func<PlainSource, PlainTarget>> Project();

                public static bool Run()
                {
                    var failures = 0;
                    Parallel.For(0, 256, value =>
                    {
                        var source = new Source { Value = value };
                        source.Next = source;
                        var direct = Map(source);
                        var runtime = (Target)MapRuntime(source, typeof(Target));
                        if (!ReferenceEquals(direct, direct.Next)
                            || !ReferenceEquals(runtime, runtime.Next)
                            || !ReferenceEquals(Project(), Project()))
                            Interlocked.Increment(ref failures);
                    });
                    return failures == 0;
                }
            }
            public sealed class Source { public int Value { get; set; } public Source? Next { get; set; } }
            public sealed class Target { public int Value { get; set; } public Target? Next { get; set; } }
            public sealed record PlainSource(int Value);
            public sealed record PlainTarget(int Value);
            """,
            projectionReference
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<bool>(result, "Mapper", "Run").ShouldBeTrue();
    }

    [Fact]
    public void GeneratesClosedWorldRuntimeDispatchWithoutReflection()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);

                public static string Run()
                {
                    var mapped = (Target)MapRuntime(new Source(42), typeof(Target));
                    var known = TryMapRuntime(new Source(7), typeof(Target), out var second);
                    var unknown = TryMapRuntime(new Source(1), typeof(string), out _);
                    return $"{mapped.Value}|{known}|{((Target?)second)?.Value}|{unknown}";
                }
            }
            public sealed record Source(int Value);
            public sealed record Target(int Value);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("source.GetType() == typeof(global::Source)");
        result.Source.ShouldNotContain("System.Reflection");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("42|True|7|False");
    }

    [Fact]
    public void DispatchesOnlyDeclaredCollectionsAndOptedInDerivedSources()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                public static partial List<Target> MapMany(List<Source> source);

                [MapRegistryDerived]
                public static partial Target MapDerived(Source source);

                public static string Run()
                {
                    var many = (List<Target>)MapRuntime(new List<Source> { new Source { Value = 4 } }, typeof(List<Target>));
                    var derived = (Target)MapRuntime(new DerivedSource { Value = 7 }, typeof(Target));
                    try
                    {
                        MapRuntime(new object(), typeof(Target));
                        return "no-error";
                    }
                    catch (InvalidOperationException error)
                    {
                        return $"{many[0].Value}|{derived.Value}|{error.Message.Contains("System.Object")}|{error.Message.Contains("Target")}";
                    }
                }
            }
            public class Source { public int Value { get; set; } }
            public sealed class DerivedSource : Source { }
            public sealed class Target { public int Value { get; set; } }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("4|7|True|True");
    }

    [Fact]
    public void RejectsDuplicateAndAmbiguousRuntimeRegistrations()
    {
        var duplicate = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                public static partial Target First(Source source);
                public static partial Target Second(Source source);
            }
            public sealed record Source(int Value);
            public sealed record Target(int Value);
            """
        );
        duplicate.Diagnostics.ShouldContain(x => x.Id == "DMPR107" && x.GetMessage().Contains("more than once"));

        var ambiguous = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                [MapRegistryDerived] public static partial Target MapBase(BaseSource source);
                [MapRegistryDerived] public static partial Target MapDerived(DerivedSource source);
            }
            public class BaseSource { public int Value { get; set; } }
            public sealed class DerivedSource : BaseSource { }
            public sealed class Target { public int Value { get; set; } }
            """
        );
        ambiguous.Diagnostics.ShouldContain(x => x.Id == "DMPR107" && x.GetMessage().Contains("overlap"));
    }

    [Fact]
    public void RejectsRuntimeRegistrationsWhoseUnrelatedInterfacesCanOverlap()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                [MapRegistryDerived] public static partial Target MapFirst(IFirst source);
                [MapRegistryDerived] public static partial Target? MapSecond(ISecond source);
            }
            public interface IFirst { int Value { get; } }
            public interface ISecond { int Value { get; } }
            public sealed record Target(int Value);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR107" && x.GetMessage().Contains("overlap"));
    }

    [Fact]
    public void RejectsRuntimeRegistryNamesInheritedFromABaseMapper()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;
            public class MapperBase
            {
                protected static object MapRuntime(object source, Type targetType) => source;
            }
            [DomainMapper]
            [MapRegistry]
            public partial class Mapper : MapperBase
            {
                public static partial Target Map(Source source);
            }
            public sealed record Source(int Value);
            public sealed record Target(int Value);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR107" && x.GetMessage().Contains("must be available"));
    }

    [Fact]
    public void GeneratesRuntimeRegistrySyntaxForNullableReferenceAnnotations()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;
            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                public static partial string? Map(string source);
                public static partial int? MapNullable(int? source);
                public static bool Run() =>
                    TryMapRuntime("mapped", typeof(string), out var target)
                    && (string?)target == "mapped"
                    && TryMapRuntime(42, typeof(int?), out var nullableTarget)
                    && (int?)nullableTarget == 42;
            }
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldNotContain("typeof(string?)");
        GeneratorTestHarness.InvokeStatic<bool>(result, "Mapper", "Run").ShouldBeTrue();
    }

    [Fact]
    public void RejectsDerivedRuntimeDispatchForValueTypeSources()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                [MapRegistryDerived]
                public static partial int Map(int source);
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR107" && x.GetMessage().Contains("reference-type source"));
    }

    [Fact]
    public void RejectsRegistryPairsThatCollapseThroughNullableValueBoxing()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                public static partial int? MapValue(int source);
                public static partial int? MapNullable(int? source);
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR107" && x.GetMessage().Contains("more than once"));
    }

    [Fact]
    public void ExcludesMappingsWhoseDeferredHelpersFailFromTheRuntimeRegistry()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            [DomainMapper]
            [MapRegistry]
            public static partial class Mapper
            {
                [MapReferenceTracking]
                public static partial Target Map(Source source);
            }
            public sealed class Source { public ChildSource Child { get; set; } = new(); }
            public sealed class ChildSource { public int Value { get; set; } }
            public sealed class Target { public ChildTarget Child { get; set; } = new(); }
            public sealed class ChildTarget { public int Value { get; } }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR105");
        result.Source.ShouldNotContain("target = Map(");
    }

    [Fact]
    public void GeneratesAndCachesAnInspectableTypedProjection()
    {
        var projectionReference = MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location);
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Linq.Expressions;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapMember(nameof(Target.Description), nameof(Source.Warehouse) + "." + nameof(Warehouse.Description))]
                public static partial Target Map(Source source);

                [MapProjection(nameof(Map))]
                public static partial Expression<Func<Source, Target>> Project();

                public static string Run()
                {
                    var first = Project();
                    var second = Project();
                    var mapped = first.Compile()(new Source(42, null));
                    return $"{ReferenceEquals(first, second)}|{mapped.Id}|{mapped.Description ?? "null"}";
                }
            }
            public sealed record Warehouse(string Description);
            public sealed record Source(int Id, Warehouse? Warehouse);
            public sealed record Target(int Id, string? Description);
            """,
            projectionReference
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("internal static readonly global::System.Linq.Expressions.Expression");
        result.Source.ShouldContain("RequiresUnreferencedCode");
        result.Source.ShouldNotContain(".Compile()(");
        GeneratorTestHarness.InvokeStatic<string>(result, "Mapper", "Run").ShouldBe("True|42|null");
    }

    [Fact]
    public void RejectsProjectionOperationsThatWouldCauseRuntimeFallback()
    {
        var projectionReference = MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location);
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Linq.Expressions;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);

                [MapAfter(nameof(Map))]
                private static void Complete(Target target) { }

                [MapProjection(nameof(Map))]
                public static partial Expression<Func<Source, Target>> Project();
            }
            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """,
            projectionReference
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR106" && x.GetMessage().Contains("completion hooks"));
        result.Source.ShouldNotContain("Expression<Func");
    }

    [Fact]
    public void RejectsProjectionWhenTheReferencedMappingFailedCompletenessValidation()
    {
        var projectionReference = MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location);
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Linq.Expressions;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            [DomainMapper]
            public static partial class Mapper
            {
                [MappingCompleteness(MappingCompleteness.Source)]
                public static partial Target Map(Source source);

                [MapProjection(nameof(Map))]
                public static partial Expression<Func<Source, Target>> Project();
            }
            public sealed record Source(int Id, int Unused);
            public sealed record Target(int Id);
            """,
            projectionReference
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR103");
        result.Diagnostics.ShouldContain(x => x.Id == "DMPR106" && x.GetMessage().Contains("invalid mapping contract"));
        result.Source.ShouldNotContain("__domainMapperProjection");
    }

    [Fact]
    public void RejectsCustomDelegateProjectionDeclarations()
    {
        var projectionReference = MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location);
        var result = GeneratorTestHarness.Generate(
            """
            using System.Linq.Expressions;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            public delegate Target CustomProjection(Source source);
            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);

                [MapProjection(nameof(Map))]
                public static partial Expression<CustomProjection> Project();
            }
            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """,
            projectionReference
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR106" && x.GetMessage().Contains("declared method shape"));
        result.Source.ShouldNotContain("__domainMapperProjection");
    }

    [Fact]
    public void RejectsUserDefinedConversionsInProviderNeutralProjections()
    {
        var projectionReference = MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location);
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Linq.Expressions;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);

                [MapProjection(nameof(Map))]
                public static partial Expression<Func<Source, Target>> Project();
            }
            public sealed record Source(WrappedInt Value);
            public sealed record Target(int Value);
            public sealed record WrappedInt(int Value)
            {
                public static implicit operator int(WrappedInt value) => value.Value;
            }
            """,
            projectionReference
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR106" && x.GetMessage().Contains("unsupported construction"));
        result.Source.ShouldNotContain("__domainMapperProjection");
    }

    [Fact]
    public void GeneratesPureLiftedNullableConversionsInProjections()
    {
        var projectionReference = MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location);
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Linq.Expressions;
            using DomainMapper.Abstractions;
            using DomainMapper.Projections;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);

                [MapProjection(nameof(Map))]
                public static partial Expression<Func<Source, Target>> Project();

                public static bool Run() => Project().Compile()(new Source(42)).Value == 42L;
            }
            public sealed record Source(int? Value);
            public sealed record Target(long? Value);
            """,
            projectionReference
        );

        result.Errors.ShouldBeEmpty(result.Source);
        GeneratorTestHarness.InvokeStatic<bool>(result, "Mapper", "Run").ShouldBeTrue();
    }
}
