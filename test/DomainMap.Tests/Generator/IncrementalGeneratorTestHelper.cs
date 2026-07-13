using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Tests.Generator;

internal static class IncrementalGeneratorTestHelper
{
    public static CSharpCompilation ReplaceRecord(
        CSharpCompilation compilation,
        string recordName,
        [StringSyntax(StringSyntax.CSharp)] string newRecord
    )
    {
        var syntaxTree = compilation.SyntaxTrees.Single();
        var recordDeclaration = syntaxTree
            .GetCompilationUnitRoot()
            .Members.OfType<RecordDeclarationSyntax>()
            .Single(x => x.Identifier.Text == recordName);
        var updatedRecordDeclaration = SyntaxFactory.ParseMemberDeclaration(newRecord)!;

        var newRoot = syntaxTree.GetCompilationUnitRoot().ReplaceNode(recordDeclaration, updatedRecordDeclaration);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(), newTree);
    }

    public static void AssertRunReasons(GeneratorDriver driver, IncrementalGeneratorRunReasons reasons, int mapperIndex = 0)
    {
        var runResult = driver.GetRunResult().Results[0];
        if (mapperIndex == 0)
        {
            // compilation and defaults are built access all mappers and not per mapper,
            // only assert for the first mapper
            AssertRunReason(runResult, DomainMapGeneratorStepNames.BuildCompilationContext, reasons.CompilationStep, mapperIndex);
            AssertRunReason(runResult, DomainMapGeneratorStepNames.BuildMapperDefaults, reasons.BuildMapperDefaultsStep, mapperIndex);
            AssertRunReason(
                runResult,
                DomainMapGeneratorStepNames.BuildUseStaticDomainMappers,
                reasons.BuildUseStaticDomainMapperDefaultsStep,
                mapperIndex
            );
        }

        AssertRunReason(runResult, DomainMapGeneratorStepNames.ReportDiagnostics, reasons.ReportDiagnosticsStep, mapperIndex);
        AssertRunReason(runResult, DomainMapGeneratorStepNames.BuildMappers, reasons.BuildMappersStep, mapperIndex);
    }

    public static void AssertRunReason(
        GeneratorDriver driver,
        string stepName,
        IncrementalStepRunReason expectedStepReason,
        int outputIndex = 0
    )
    {
        var runResult = driver.GetRunResult().Results[0];
        AssertRunReason(runResult, stepName, expectedStepReason, outputIndex);
    }

    private static void AssertRunReason(
        GeneratorRunResult runResult,
        string stepName,
        IncrementalStepRunReason expectedStepReason,
        int outputIndex
    )
    {
        var actualStepReason = runResult.TrackedSteps[stepName].SelectMany(x => x.Outputs).ElementAt(outputIndex).Reason;
        actualStepReason.ShouldBe(expectedStepReason, $"step {stepName} of mapper at index {outputIndex}");
    }
}
