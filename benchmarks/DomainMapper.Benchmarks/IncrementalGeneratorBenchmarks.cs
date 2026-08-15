using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using DomainMapper.Abstractions;
using DomainMapper.Projections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(SourceGeneratorBenchmarkConfig))]
public class IncrementalGeneratorBenchmarks
{
    private GeneratorDriver? _coreDriver;
    private CSharpCompilation? _coreCompilation;
    private CSharpCompilation? _isolatedEditCompilation;
    private GeneratorDriver? _sharedDriver;
    private CSharpCompilation? _sharedEditCompilation;
    private CSharpCompilation? _registryCompilation;
    private CSharpCompilation? _projectionCompilation;

    [Params(1, 16, 64)]
    public int MappingCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_coreCompilation, _isolatedEditCompilation) = BuildIndependentFixture(MappingCount, MapperFeature.Core);
        _coreDriver = CreateDriver().RunGenerators(_coreCompilation);
        var (sharedCompilation, sharedEditCompilation) = BuildSharedFixture(MappingCount);
        _sharedDriver = CreateDriver().RunGenerators(sharedCompilation);
        _sharedEditCompilation = sharedEditCompilation;
        (_registryCompilation, _) = BuildIndependentFixture(MappingCount, MapperFeature.Registry);
        (_projectionCompilation, _) = BuildIndependentFixture(MappingCount, MapperFeature.Projection);

        Validate(_coreDriver, _coreCompilation, MappingCount, "core");
        Validate(_sharedDriver, sharedCompilation, MappingCount, "shared-contract");
        Validate(CreateDriver(), _registryCompilation, MappingCount, "registry");
        Validate(CreateDriver(), _projectionCompilation, MappingCount, "projection");
    }

    [Benchmark]
    [BenchmarkCategory("ColdCore")]
    public object ColdCore() => CreateDriver().RunGenerators(_coreCompilation!);

    [Benchmark]
    [BenchmarkCategory("NoOpCore")]
    public object NoOpCore() => _coreDriver!.RunGenerators(_coreCompilation!);

    [Benchmark]
    [BenchmarkCategory("IsolatedContractEdit")]
    public object IsolatedContractEdit() => _coreDriver!.RunGenerators(_isolatedEditCompilation!);

    [Benchmark]
    [BenchmarkCategory("SharedContractEdit")]
    public object SharedContractEdit() => _sharedDriver!.RunGenerators(_sharedEditCompilation!);

    [Benchmark]
    [BenchmarkCategory("ColdRegistry")]
    public object ColdRegistry() => CreateDriver().RunGenerators(_registryCompilation!);

    [Benchmark]
    [BenchmarkCategory("ColdProjection")]
    public object ColdProjection() => CreateDriver().RunGenerators(_projectionCompilation!);

    private static (CSharpCompilation Initial, CSharpCompilation IsolatedEdit) BuildIndependentFixture(
        int mappingCount,
        MapperFeature feature
    )
    {
        var trees = new List<SyntaxTree>();
        for (var index = 0; index < mappingCount; index++)
        {
            trees.Add(Parse(Contract(index, "int"), $"Contract{index}.cs"));
            trees.Add(Parse(Mapper(index, feature), $"Mapper{index}.cs"));
        }
        var initial = BuildCompilation($"DomainMapper.{feature}.{mappingCount}", trees, feature == MapperFeature.Projection);
        var editedTree = Parse(Contract(0, "long"), "Contract0.cs");
        return (initial, initial.ReplaceSyntaxTree(trees[0], editedTree));
    }

    private static (CSharpCompilation Initial, CSharpCompilation SharedEdit) BuildSharedFixture(int mappingCount)
    {
        var shared = Parse("public sealed record SharedSource(int Value);", "SharedContract.cs");
        var trees = new List<SyntaxTree> { shared };
        for (var index = 0; index < mappingCount; index++)
        {
            trees.Add(Parse($"public sealed record Target{index}(int Value);", $"Target{index}.cs"));
            trees.Add(
                Parse(
                    $"using DomainMapper.Abstractions; [DomainMapper] public static partial class Mapper{index} {{ public static partial Target{index} Map(SharedSource source); }}",
                    $"Mapper{index}.cs"
                )
            );
        }
        var initial = BuildCompilation($"DomainMapper.Shared.{mappingCount}", trees, false);
        return (initial, initial.ReplaceSyntaxTree(shared, Parse("public sealed record SharedSource(long Value);", "SharedContract.cs")));
    }

    private static CSharpCompilation BuildCompilation(string name, IEnumerable<SyntaxTree> trees, bool includeProjectionReference)
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator)
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = trustedPlatformAssemblies.Select(path => MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(DomainMapperAttribute).Assembly.Location));
        if (includeProjectionReference)
            references.Add(MetadataReference.CreateFromFile(typeof(MapProjectionAttribute).Assembly.Location));
        return CSharpCompilation.Create(
            name,
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );
    }

    private static CSharpGeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create(
            [new DomainMapperGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
        );

    private static void Validate(GeneratorDriver driver, Compilation compilation, int expectedOutputs, string fixture)
    {
        var completed = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var errors = diagnostics.Concat(output.GetDiagnostics()).Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException($"{fixture} fixture failed: {string.Join(Environment.NewLine, errors.AsEnumerable())}");
        if (completed.GetRunResult().GeneratedTrees.Length != expectedOutputs)
            throw new InvalidOperationException($"{fixture} fixture generated an unexpected output count.");
    }

    private static SyntaxTree Parse(string source, string path) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview), path);

    private static string Contract(int index, string scalarType) =>
        $"public sealed record Source{index}({scalarType} Value); public sealed record Target{index}({scalarType} Value);";

    private static string Mapper(int index, MapperFeature feature)
    {
        var registry = feature == MapperFeature.Registry ? "[MapRegistry]" : string.Empty;
        var projectionUsing =
            feature == MapperFeature.Projection
                ? "using System; using System.Linq.Expressions; using DomainMapper.Projections;"
                : string.Empty;
        var projection =
            feature == MapperFeature.Projection
                ? $"[MapProjection(nameof(Map))] public static partial Expression<Func<Source{index}, Target{index}>> Project();"
                : string.Empty;
        return $"using DomainMapper.Abstractions; {projectionUsing} [DomainMapper] {registry} public static partial class Mapper{index} {{ public static partial Target{index} Map(Source{index} source); {projection} }}";
    }

    private enum MapperFeature
    {
        Core,
        Registry,
        Projection,
    }
}
