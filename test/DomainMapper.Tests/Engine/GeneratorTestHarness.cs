using System.Collections.Immutable;
using DomainMapper.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Tests.Engine;

internal static class GeneratorTestHarness
{
    public static GenerationResult Generate(string source, params MetadataReference[] additionalReferences)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = TrustedPlatformReferences()
            .Add(MetadataReference.CreateFromFile(typeof(DomainMapperAttribute).Assembly.Location))
            .AddRange(additionalReferences);
        var compilation = CSharpCompilation.Create(
            $"DomainMapper.ContractTests.{Guid.NewGuid():N}",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );
        var driver = CSharpGeneratorDriver.Create([new DomainMapperGenerator().AsSourceGenerator()], parseOptions: parseOptions);

        driver = (CSharpGeneratorDriver)
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedTrees = driver.GetRunResult().GeneratedTrees;
        var generatedSource = string.Join(Environment.NewLine, generatedTrees.Select(x => x.GetText().ToString()));
        var compilationDiagnostics = outputCompilation.GetDiagnostics();
        var allDiagnostics = generatorDiagnostics.AddRange(compilationDiagnostics);
        var errors = allDiagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        var warnings = allDiagnostics.Where(x => x.Severity == DiagnosticSeverity.Warning).ToImmutableArray();
        return new GenerationResult(generatedSource, generatorDiagnostics, errors, warnings, generatedTrees.Length, outputCompilation);
    }

    public static T InvokeStatic<T>(GenerationResult result, string typeName, string methodName)
    {
        using var stream = new MemoryStream();
        var emitResult = result.Compilation.Emit(stream);
        if (!emitResult.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, emitResult.Diagnostics));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var type = assembly.GetType(typeName) ?? throw new InvalidOperationException($"Type '{typeName}' was not found.");
        var method =
            type.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method '{typeName}.{methodName}' was not found.");
        return (T)(method.Invoke(null, null) ?? throw new InvalidOperationException($"Method '{typeName}.{methodName}' returned null."));
    }

    public static MetadataReference CompileReference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static ImmutableArray<MetadataReference> TrustedPlatformReferences()
    {
        var paths =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator)
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return paths.Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();
    }
}

internal sealed record GenerationResult(
    string Source,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<Diagnostic> Errors,
    ImmutableArray<Diagnostic> Warnings,
    int GeneratedTreeCount,
    Compilation Compilation
);
