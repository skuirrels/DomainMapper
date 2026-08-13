using Microsoft.CodeAnalysis;

namespace DomainMapper.Tests.Engine;

public sealed class GeneratorRegressionTests
{
    [Fact]
    public void PreservesGenericMethodContextInNestedHelpers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target<T> Map<T>(Source<T> source) where T : class, new();
            }

            public sealed record ChildSource<T>(T Value);
            public sealed record Source<T>(ChildSource<T> Child);
            public sealed record ChildTarget<T>(T Value);
            public sealed record Target<T>(ChildTarget<T> Child);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("MapToChildTarget<T>");
        result.Source.ShouldContain("where T : class, new()");
    }

    [Fact]
    public void InitializesRequiredPropertiesDuringConstruction()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(string Name);
            public sealed class Target { public required string Name { get; set; } }
            public static class Probe
            {
                public static string Run() => Mapper.Map(new Source("mapped")).Name;
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("new global::Target() { Name = source.Name }");
        GeneratorTestHarness.InvokeStatic<string>(result, "Probe", "Run").ShouldBe("mapped");
    }

    [Fact]
    public void RejectsScalarConstructionThatWouldDropWritableState()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(int source);
            }

            public sealed class Target
            {
                public Target(int value) => Value = value;
                public int Value { get; }
                public string Name { get; set; } = string.Empty;
            }
            """
        );

        result.Errors.ShouldContain(x => x.Id == "DMPR101");
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void RequiresRefForValueTypeUpdateTargets()
    {
        var invalid = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial void Update(Source source, Target target);
            }

            public sealed record Source(int Value);
            public struct Target { public int Value { get; set; } }
            """
        );
        invalid.Errors.ShouldContain(x => x.Id == "DMPR100");

        var readOnly = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial void Update(Source source, in Target target);
            }

            public sealed record Source(int Value);
            public struct Target { public int Value { get; set; } }
            """
        );
        readOnly.Errors.ShouldContain(x => x.Id == "DMPR100");

        var valid = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial void Update(Source source, ref Target target);
            }

            public sealed record Source(int Value);
            public struct Target { public int Value { get; set; } }
            public static class Probe
            {
                public static int Run()
                {
                    var target = new Target();
                    Mapper.Update(new Source(42), ref target);
                    return target.Value;
                }
            }
            """
        );

        valid.Errors.ShouldBeEmpty();
        GeneratorTestHarness.InvokeStatic<int>(valid, "Probe", "Run").ShouldBe(42);
    }

    [Fact]
    public void UsesImplementedCollectionContractsForExplicitImplementations()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections;
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target[] Map(ExplicitList source);
            }

            public sealed record Source(int Value);
            public sealed record Target(int Value);
            public sealed class ExplicitList : IReadOnlyList<Source>
            {
                private readonly Source[] values = [new(42)];
                int IReadOnlyCollection<Source>.Count => values.Length;
                Source IReadOnlyList<Source>.this[int index] => values[index];
                IEnumerator<Source> IEnumerable<Source>.GetEnumerator() => ((IEnumerable<Source>)values).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => values.GetEnumerator();
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("((global::System.Collections.Generic.IReadOnlyCollection<global::Source>)source).Count");
        result.Source.ShouldContain("((global::System.Collections.Generic.IReadOnlyList<global::Source>)source)[i]");
    }

    [Fact]
    public void DoesNotTreatExplicitInterfacePropertiesAsConcreteMembers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            public interface ISource { int Id { get; } }
            public sealed class Source : ISource { int ISource.Id => 42; }
            public sealed record Target(int Id);

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }
            """
        );

        result.Errors.ShouldContain(x => x.Id == "DMPR101");
        result.Errors.ShouldNotContain(x => x.Id == "CS1061");
    }

    [Fact]
    public void AvoidsHelperNamesAlreadyDeclaredByTheMapper()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                private static ChildTarget MapToChildTarget(ChildSource source) => new(-1);
                public static partial Target Map(Source source);
            }

            public sealed record ChildSource(int Id);
            public sealed record Source(ChildSource Child);
            public sealed record ChildTarget(int Id);
            public sealed record Target(ChildTarget Child);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("MapToChildTarget2");
    }

    [Fact]
    public void PreservesVirtualPartialMethodModifiers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public partial class Mapper
            {
                public virtual partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("public virtual partial global::Target Map");
    }

    [Fact]
    public void PreservesOverrideSealedAndNewPartialMethodModifiers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            public class VirtualBase
            {
                public virtual Target Map(Source source) => new(source.Id);
            }
            [DomainMapper]
            public partial class OverrideMapper : VirtualBase
            {
                public override partial Target Map(Source source);
            }

            public class SealedBase
            {
                public virtual Target Map(Source source) => new(source.Id);
            }
            [DomainMapper]
            public partial class SealedMapper : SealedBase
            {
                public sealed override partial Target Map(Source source);
            }

            public class NewBase
            {
                public Target Map(Source source) => new(source.Id);
            }
            [DomainMapper]
            public partial class NewMapper : NewBase
            {
                public new partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("public override partial global::Target Map");
        result.Source.ShouldContain("public sealed override partial global::Target Map");
        result.Source.ShouldContain("public new partial global::Target Map");
    }

    [Fact]
    public void CarriesAmbientFactoryValuesIntoCollectionElementHelpers()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory]
                private static ItemTarget CreateItem(int id, string tenantId) => new(id, tenantId);

                [MapToFactory(nameof(Target.Create))]
                public static partial Target Map(Source source, string tenantId);
            }

            public sealed record ItemSource(int Id);
            public sealed record Source(List<ItemSource> Items);
            public sealed record ItemTarget(int Id, string TenantId);
            public sealed record Target(List<ItemTarget> Items)
            {
                public static Target Create(List<ItemTarget> items) => new(items);
            }
            public static class Probe
            {
                public static string Run() => Mapper.Map(new Source([new(42)]), "tenant").Items[0].TenantId;
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("MapToListOfItemTarget(source.Items, tenantId");
        result.Source.ShouldContain("CreateItem(item.Id, __ambient0)");
        GeneratorTestHarness.InvokeStatic<string>(result, "Probe", "Run").ShouldBe("tenant");
    }

    [Fact]
    public void UsesImplementedDictionaryContractsForExplicitImplementations()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections;
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Dictionary<int, Target> Map(ExplicitDictionary source);
            }

            public sealed record Source(int Value);
            public sealed record Target(int Value);
            public sealed class ExplicitDictionary : IReadOnlyDictionary<int, Source>
            {
                private readonly Dictionary<int, Source> values = new() { [1] = new(42) };
                int IReadOnlyCollection<KeyValuePair<int, Source>>.Count => values.Count;
                IEnumerable<int> IReadOnlyDictionary<int, Source>.Keys => values.Keys;
                IEnumerable<Source> IReadOnlyDictionary<int, Source>.Values => values.Values;
                Source IReadOnlyDictionary<int, Source>.this[int key] => values[key];
                bool IReadOnlyDictionary<int, Source>.ContainsKey(int key) => values.ContainsKey(key);
                bool IReadOnlyDictionary<int, Source>.TryGetValue(int key, out Source value) => values.TryGetValue(key, out value!);
                IEnumerator<KeyValuePair<int, Source>> IEnumerable<KeyValuePair<int, Source>>.GetEnumerator() => values.GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => values.GetEnumerator();
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("((global::System.Collections.Generic.IReadOnlyDictionary<");
        result.Source.ShouldContain(">)source).Count");
    }

    [Fact]
    public void RejectsFileLocalMappersWithAGeneratorDiagnostic()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            file static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """
        );

        result.Errors.ShouldContain(x => x.Id == "DMPR100");
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void IncludesGeneratorDiagnosticsInTheErrorGate()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory]
                private Target Create(int id) => new(id);

                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR100");
        result.Errors.ShouldContain(x => x.Id == "DMPR100");
    }

    [Fact]
    public void MapsWritableMembersNotConsumedByConstructor()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id, string Name);
            public sealed class Target
            {
                public Target(int id) => Id = id;
                public int Id { get; }
                public string Name { get; set; } = string.Empty;
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("var target = new global::Target(source.Id);");
        result.Source.ShouldContain("target.Name = source.Name;");
    }

    [Fact]
    public void RejectsUnmatchedWritableTargetMembersInsteadOfSilentlyDroppingThem()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed class Target
            {
                public int Id { get; set; }
                public string Name { get; set; } = string.Empty;
            }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR101" && x.Severity == DiagnosticSeverity.Error);
        result.Source.ShouldBeEmpty();
    }

    [Fact]
    public void MapsEnumerableSourcesWithoutAssumingCountOrIndexer()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial List<Target> ToList(IEnumerable<Source> source);
                public static partial Target[] ToArray(IEnumerable<Source> source);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("foreach (var item in source)");
        result.Source.ShouldContain("return target.ToArray();");
        result.Source.ShouldNotContain("source.Count");
        result.Source.ShouldNotContain("source[i]");
    }

    [Fact]
    public void MapsMutableAndReadOnlyDictionaryInterfacesThroughConcreteDictionaries()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial IDictionary<int, Target> ToMutable(Dictionary<int, Source> source);
                public static partial IReadOnlyDictionary<int, Target> ToReadOnly(Dictionary<int, Source> source);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("new global::System.Collections.Generic.Dictionary");
        result.Source.ShouldContain("source.Count");
        result.Source.ShouldNotContain("new global::System.Collections.Generic.IDictionary");
        result.Source.ShouldNotContain("new global::System.Collections.Generic.IReadOnlyDictionary");
    }

    [Fact]
    public void UsesUniqueHintNamesForMappersWithTheSameSimpleName()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            namespace A
            {
                [DomainMapper]
                public static partial class Mapper { public static partial Target Map(Source source); }
                public sealed record Source(int Id);
                public sealed record Target(int Id);
            }

            namespace B
            {
                [DomainMapper]
                public static partial class Mapper { public static partial Target Map(Source source); }
                public sealed record Source(int Id);
                public sealed record Target(int Id);
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.GeneratedTreeCount.ShouldBe(2);
        result.Warnings.ShouldNotContain(x => x.Id == "CS8785");
    }

    [Fact]
    public void PreservesNestedGenericRecordAndMethodShape()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            public readonly partial struct Container<TOuter> where TOuter : class
            {
                [DomainMapper]
                public partial record Mapper<TValue> where TValue : notnull
                {
                    public partial Target<TOuter, TValue, TResult> Map<TResult>(Source<TOuter, TValue, TResult> source)
                        where TResult : class, new();
                }
            }

            public sealed record Source<TOuter, TValue, TResult>(TOuter Outer, TValue Value, TResult Result);
            public sealed record Target<TOuter, TValue, TResult>(TOuter Outer, TValue Value, TResult Result);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("readonly partial struct Container<TOuter> where TOuter : class");
        result.Source.ShouldContain("partial record Mapper<TValue> where TValue : notnull");
        result.Source.ShouldContain("Map<TResult>");
        result.Source.ShouldContain("where TResult : class, new()");
    }

    [Fact]
    public void PreservesRefAndNullableMethodSignatures()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial void Update(Source source, ref MutableTarget target);
                public static partial NullableTarget? Map(Source? source);
            }

            public sealed record Source(int Id);
            public sealed class MutableTarget { public int Id { get; set; } }
            public sealed record NullableTarget(int Id);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldNotContain(x => x.Id == "CS8611");
        result.Source.ShouldContain("ref global::MutableTarget target");
        result.Source.ShouldContain("global::NullableTarget? Map(global::Source? source)");
        result.Source.ShouldContain("source is null ? null");
    }

    [Fact]
    public void MapsInheritedPropertiesAndPrefersExactCase()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }

            public class SourceBase { public int Id { get; set; } }
            public sealed class Source : SourceBase { public int id { get; set; } }
            public class TargetBase { public int Id { get; set; } }
            public sealed class Target : TargetBase { }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("target.Id = source.Id;");
        result.Source.Contains("target.Id = source.id;", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void GeneratesDistinctObjectAndSequenceHelperNames()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial RootTarget Map(RootSource source);
            }

            public sealed record ChildSource(int Id);
            public sealed record RootSource(ChildSource A, ChildSource B, List<ChildSource> Array, List<ChildSource> Sequence);
            public sealed class RootTarget
            {
                public A.Target A { get; set; } = null!;
                public B.Target B { get; set; } = null!;
                public ItemTarget[] Array { get; set; } = [];
                public IEnumerable<ItemTarget> Sequence { get; set; } = [];
            }
            public sealed record ItemTarget(int Id);
            namespace A { public sealed record Target(int Id); }
            namespace B { public sealed record Target(int Id); }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("MapToTarget2");
        result.Source.ShouldContain("MapToSequenceOfItemTarget2");
    }

    [Fact]
    public void BindsAdditionalMappingParametersToTargetFactory()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapToFactory(nameof(Target.Create))]
                public static partial Target Map(Source source, string tenantId);
            }

            public sealed record Source(int Id);
            public sealed record Target(int Id, string TenantId)
            {
                public static Target Create(int id, string tenantId) => new(id, tenantId);
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("global::Target.Create(source.Id, tenantId)");
    }

    [Fact]
    public void HonorsMembersDomainFactoryInput()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory]
                private static ValueTarget CreateValue(int id, string name) => new(id, name);

                public static partial Target Map(Source source);
            }

            public sealed record ValueSource(int Id, string Name);
            public sealed record Source(ValueSource Value);
            public sealed record ValueTarget(int Id, string Name);
            public sealed record Target(ValueTarget Value);
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("CreateValue(source.Value.Id, source.Value.Name)");
    }

    [Fact]
    public void BindsAdditionalMappingParametersToMembersDomainFactory()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory]
                private static TenantId CreateId(int id, string tenantId) => new(id, tenantId);

                [MapToFactory(nameof(Target.Create))]
                public static partial Target Map(Source source, string tenantId);
            }

            public sealed record Source(int Id);
            public sealed record TenantId(int Id, string TenantIdValue);
            public sealed record Target(TenantId Id)
            {
                public static Target Create(TenantId id) => new(id);
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("CreateId(source.Id, tenantId)");
    }

    [Fact]
    public void PrefersExplicitMappingParametersAtTheTargetFactoryBoundary()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [MapToFactory(nameof(Target.Create))]
                public static partial Target Map(Source source, string tenantId);
            }

            public sealed record Source(int Id, string TenantId);
            public sealed record Target(int Id, string TenantId)
            {
                public static Target Create(int id, string tenantId) => new(id, tenantId);
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("global::Target.Create(source.Id, tenantId)");
    }

    [Fact]
    public void PrefersNestedSourceMembersOverAmbientMappingParameters()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory]
                private static ValueTarget CreateValue(int id, string tenantId) => new(id, tenantId);

                [MapToFactory(nameof(Target.Create))]
                public static partial Target Map(Source source, string tenantId);
            }

            public sealed record ValueSource(int Id, string TenantId);
            public sealed record Source(ValueSource Value);
            public sealed record ValueTarget(int Id, string TenantId);
            public sealed record Target(ValueTarget Value)
            {
                public static Target Create(ValueTarget value) => new(value);
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("CreateValue(source.Value.Id, source.Value.TenantId)");
    }

    [Fact]
    public void RejectsAnInstanceDomainFactoryInsteadOfGeneratingAnInstanceCallFromStaticCode()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private TargetId ToId(int value) => new(value);

                public static partial Target Map(Source source);
            }

            public sealed record Source(int Id);
            public sealed record TargetId(int Value);
            public sealed record Target(TargetId Id);
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR100" && x.Severity == DiagnosticSeverity.Error);
        result.Errors.ShouldNotContain(x => x.Id == "CS0120");
    }

    [Fact]
    public void BacktracksToAUsableConstructorWhenALongerCandidateCannotBeMapped()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper { public static partial Target Map(Source source); }

            public sealed record Source(int Id, BadSource Value);
            public sealed record BadSource(string Text);
            public sealed class Unconstructible { private Unconstructible(string value) { } }
            public sealed class Target
            {
                public Target(Unconstructible value, int id) => Id = id;
                public Target(int id) => Id = id;
                public int Id { get; }
            }
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Source.ShouldContain("new global::Target(source.Id)");
    }

    [Fact]
    public void EmitsValidMappingsEvenWhenAnotherMappingContractIsInvalid()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial GoodTarget Good(GoodSource source);
                public static partial BadTarget Bad(BadSource source);
            }

            public sealed record GoodSource(int Id);
            public sealed record GoodTarget(int Id);
            public sealed record BadSource(int Id);
            public sealed class BadTarget { private BadTarget(int id) { } }
            """
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR101");
        result.GeneratedTreeCount.ShouldBe(1);
        result.Source.ShouldContain("Good(global::GoodSource source)");
    }

    [Fact]
    public void RejectsMembersThatAreNotAccessibleFromTheConsumerAssembly()
    {
        var externalReference = GeneratorTestHarness.CompileReference(
            "ExternalDomain",
            """
            namespace ExternalDomain;
            public sealed class Target
            {
                public Target() { }
                public int Id { get; internal set; }
            }
            """
        );
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;
            using ExternalDomain;

            [DomainMapper]
            public static partial class Mapper { public static partial Target Map(Source source); }
            public sealed record Source(int Id);
            """,
            externalReference
        );

        result.Diagnostics.ShouldContain(x => x.Id == "DMPR101" && x.Severity == DiagnosticSeverity.Error);
    }
}
