using System.Runtime.Loader;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using DomainMap.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMap.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(SourceGeneratorBenchmarkConfig))]
public class SourceGeneratorBenchmarks
{
    private const string DomainMapAttribute = "DomainMap.Abstractions.DomainMapper";
    private const string MapperlyAttribute = "Riok.Mapperly.Abstractions.Mapper";

    private const string FixtureSource = """
        using System;
        using System.Collections.Generic;

        [__MAPPER_ATTRIBUTE__]
        public static partial class MappingFixture
        {
            public static partial PrimitiveTarget MapPrimitive(PrimitiveSource source);
            public static partial NullableTarget MapNullable(NullableSource source);
            public static partial CustomerTarget MapCustomer(CustomerSource source);
            public static partial AddressTarget MapAddress(AddressSource source);
            public static partial LineTarget MapLine(LineSource source);
            public static partial OrderTarget MapOrder(OrderSource source);
            public static partial PrimitiveTarget[] MapArray(PrimitiveSource[] source);
            public static partial List<CustomerTarget> MapList(List<CustomerSource> source);
            public static partial IReadOnlyCollection<LineTarget> MapCollection(IReadOnlyCollection<LineSource> source);
            public static partial Dictionary<string, PrimitiveTarget> MapDictionary(Dictionary<string, PrimitiveSource> source);
            public static partial GenericTarget<string> MapGeneric(GenericSource<string> source);
            public static partial void UpdatePrimitive(PrimitiveSource source, PrimitiveTarget target);
        }

        public sealed record PrimitiveSource(int Id, string Name, decimal Amount, DateTimeOffset CreatedAt);
        public sealed class PrimitiveTarget
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
        }

        public sealed record NullableSource(string? Value, int? Count);
        public sealed record NullableTarget(string? Value, int? Count);
        public sealed record AddressSource(string Line1, string City, string PostalCode);
        public sealed record AddressTarget(string Line1, string City, string PostalCode);
        public sealed record CustomerSource(Guid Id, string Name, AddressSource Address);
        public sealed record CustomerTarget(Guid Id, string Name, AddressTarget Address);
        public sealed record LineSource(string Sku, int Quantity, decimal UnitPrice);
        public sealed record LineTarget(string Sku, int Quantity, decimal UnitPrice);
        public sealed record OrderSource(Guid Id, CustomerSource Customer, List<LineSource> Lines);
        public sealed record OrderTarget(Guid Id, CustomerTarget Customer, List<LineTarget> Lines);
        public sealed record GenericSource<T>(T Value);
        public sealed record GenericTarget<T>(T Value);
        """;

    private GeneratorDriver? _domainMapDriver;
    private Compilation? _domainMapCompilation;
    private GeneratorDriver? _mapperlyDriver;
    private Compilation? _mapperlyCompilation;

    [GlobalSetup]
    public void Setup()
    {
        _domainMapCompilation = BuildCompilation(DomainMapAttribute, typeof(DomainMapperAttribute).Assembly.Location);
        _mapperlyCompilation = BuildCompilation(MapperlyAttribute, typeof(Riok.Mapperly.Abstractions.MapperAttribute).Assembly.Location);

        _domainMapDriver = CreateDriver(new DomainMapGenerator().AsSourceGenerator());
        _mapperlyDriver = CreateDriver(LoadMapperlyGenerator());

        ValidateGenerator(_domainMapDriver, _domainMapCompilation, "DomainMap");
        ValidateGenerator(_mapperlyDriver, _mapperlyCompilation, "Mapperly");
    }

    private static CSharpCompilation BuildCompilation(string mapperAttribute, string abstractionsAssemblyPath)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(FixtureSource.Replace("__MAPPER_ATTRIBUTE__", mapperAttribute), parseOptions);
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator)
            ?? throw new InvalidOperationException("Trusted platform assemblies are not available");
        var references = trustedPlatformAssemblies.Select(path => MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(abstractionsAssemblyPath));

        return CSharpCompilation.Create(
            $"{mapperAttribute}.GeneratorBenchmark",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );
    }

    private static CSharpGeneratorDriver CreateDriver(ISourceGenerator generator) =>
        CSharpGeneratorDriver.Create([generator], parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static ISourceGenerator LoadMapperlyGenerator()
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Riok.Mapperly.dll");
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        var generatorType = assembly.GetType("Riok.Mapperly.MapperGenerator", throwOnError: true)!;
        var generator = Activator.CreateInstance(generatorType) as IIncrementalGenerator;
        return generator?.AsSourceGenerator()
            ?? throw new InvalidOperationException($"{generatorType.FullName} is not an incremental source generator");
    }

    private static void ValidateGenerator(GeneratorDriver driver, Compilation compilation, string name)
    {
        var completedDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        var errors = diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException($"{name} generator failed: {string.Join(Environment.NewLine, errors.AsEnumerable())}");

        if (completedDriver.GetRunResult().GeneratedTrees.Length == 0)
            throw new InvalidOperationException($"{name} did not generate any source");
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ColdGenerator")]
    public object MapperlyColdGeneration() => _mapperlyDriver!.RunGeneratorsAndUpdateCompilation(_mapperlyCompilation!, out _, out _);

    [Benchmark]
    [BenchmarkCategory("ColdGenerator")]
    public object DomainMapColdGeneration() => _domainMapDriver!.RunGeneratorsAndUpdateCompilation(_domainMapCompilation!, out _, out _);
}
