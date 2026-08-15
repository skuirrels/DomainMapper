using DomainMapper.Engine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DomainMapper;

[Generator]
public sealed class DomainMapperGenerator : IIncrementalGenerator
{
    private const string MapperAttribute = "DomainMapper.Abstractions.DomainMapperAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var mapperTypes = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                MapperAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) =>
                    MapperGenerationInput.Create(
                        (INamedTypeSymbol)attributeContext.TargetSymbol,
                        attributeContext.SemanticModel.Compilation
                    )
            )
            .WithComparer(MapperGenerationInputComparer.Instance)
            .WithTrackingName("MapperContracts");

        context.RegisterSourceOutput(
            mapperTypes,
            static (productionContext, input) =>
            {
                var result = MapperCompiler.Compile(input.MapperType, input.Compilation, productionContext.CancellationToken);
                foreach (var diagnostic in result.Diagnostics)
                {
                    productionContext.ReportDiagnostic(diagnostic);
                }

                if (result.Source != null)
                {
                    productionContext.AddSource(result.HintName, SourceText.From(result.Source, System.Text.Encoding.UTF8));
                }
            }
        );
    }
}
