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
        // The transform performs the complete generation and yields plain data. Holding symbols or the
        // compilation in the cached value would keep every previous compilation alive in the IDE, so the
        // driver instead compares emitted results and re-adds only the mappers whose output changed.
        var mappers = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                MapperAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    MapperCompiler.Compile(
                        (INamedTypeSymbol)attributeContext.TargetSymbol,
                        attributeContext.SemanticModel.Compilation,
                        cancellationToken
                    )
            )
            .WithTrackingName("MapperContracts");

        context.RegisterSourceOutput(
            mappers,
            static (productionContext, result) =>
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
                }

                if (result.Source != null)
                {
                    productionContext.AddSource(result.HintName, SourceText.From(result.Source, System.Text.Encoding.UTF8));
                }
            }
        );
    }
}
