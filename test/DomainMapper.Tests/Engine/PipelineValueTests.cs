using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Tests.Engine;

/// <summary>
/// The incremental driver keeps the previous pipeline value whenever the new one compares equal, so anything the
/// value references stays alive for as long as the mapper is cached. These tests prove the cached value is plain data.
/// </summary>
public sealed class PipelineValueTests
{
    private static readonly Type[] ForbiddenReferences =
    [
        typeof(Compilation),
        typeof(SemanticModel),
        typeof(ISymbol),
        typeof(SyntaxTree),
        typeof(SyntaxNode),
        typeof(SyntaxReference),
        typeof(Location),
        typeof(Diagnostic),
    ];

    [Fact]
    public void CachedPipelineValuesHoldNoCompilationOrSymbolReferences()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var mapper = CSharpSyntaxTree.ParseText(
            """
            using DomainMapper.Abstractions;
            [DomainMapper] public static partial class ValidMapper { public static partial Target Map(Source source); }
            [DomainMapper] public static partial class InvalidMapper { public static partial Target Map(Unrelated source); }
            public sealed record Source(int Value);
            public sealed record Target(int Value);
            public sealed record Unrelated(string Name);
            """,
            parseOptions,
            "Mapper.cs"
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DomainMapperGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true),
            parseOptions: parseOptions
        );
        driver = driver.RunGenerators(GeneratorTestHarness.CreateCompilation([mapper]));

        var values = driver
            .GetRunResult()
            .Results.Single()
            .TrackedSteps["MapperContracts"]
            .SelectMany(x => x.Outputs)
            .Select(x => x.Value)
            .ToArray();

        values.Length.ShouldBe(2);
        var visited = new HashSet<Type>();
        foreach (var value in values)
        {
            AssertHoldsOnlyPlainData(value.ShouldNotBeNull().GetType(), visited, value.GetType().Name);
        }
    }

    [Fact]
    public void RehydratedDiagnosticsKeepTheirSourceFileAndPosition()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var mapper = CSharpSyntaxTree.ParseText(
            """
            using DomainMapper.Abstractions;
            [DomainMapper] public static partial class Mapper
            {
                public static partial Target Map(Source source);
            }
            public sealed record Source(string Name);
            public sealed record Target(int Value);
            """,
            parseOptions,
            "Mapper.cs"
        );
        var driver = CSharpGeneratorDriver.Create([new DomainMapperGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(GeneratorTestHarness.CreateCompilation([mapper]), out _, out var diagnostics);

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.Id.ShouldBe("DMPR101");
        diagnostic.GetMessage().ShouldContain("cannot construct 'Target' from 'Source'");
        var lineSpan = diagnostic.Location.GetLineSpan();
        lineSpan.Path.ShouldBe("Mapper.cs");
        lineSpan.StartLinePosition.Line.ShouldBe(3);
        mapper.GetText().ToString(diagnostic.Location.SourceSpan).ShouldBe("Map");
    }

    private static void AssertHoldsOnlyPlainData(Type type, HashSet<Type> visited, string path)
    {
        if (!visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string))
            return;

        foreach (var forbidden in ForbiddenReferences)
        {
            forbidden.IsAssignableFrom(type).ShouldBeFalse($"{path} references {forbidden.Name} through {type}");
        }
        type.ShouldNotBe(typeof(object), $"{path} is typed as object and could hold anything");

        if (type.IsArray)
        {
            AssertHoldsOnlyPlainData(type.GetElementType()!, visited, path + "[]");
            return;
        }
        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
        {
            AssertHoldsOnlyPlainData(argument, visited, $"{path}<{argument.Name}>");
        }
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            AssertHoldsOnlyPlainData(field.FieldType, visited, $"{path}.{field.Name}");
        }
    }
}
