using System.Collections.Immutable;
using DomainMap.Helpers;
using DomainMap.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap;

internal static class SyntaxProvider
{
    public static IncrementalValueProvider<ImmutableArray<Compilation>> GetNestedCompilations(
        IncrementalGeneratorInitializationContext context
    )
    {
        return context
            .MetadataReferencesProvider.OfType<MetadataReference, CompilationReference>()
            .Select((x, _) => x.Compilation)
            .Collect();
    }

    public static IncrementalValuesProvider<MapperDeclaration> GetMapperDeclarations(IncrementalGeneratorInitializationContext context)
    {
        return context
            .SyntaxProvider.ForAttributeWithMetadataName(
                DomainMapGenerator.DomainMapperAttributeName,
                static (s, _) => s is ClassDeclarationSyntax,
                static (ctx, _) => (ctx.TargetSymbol, TargetNode: (ClassDeclarationSyntax)ctx.TargetNode)
            )
            .Where(x => x.TargetSymbol is INamedTypeSymbol)
            .Select((x, _) => new MapperDeclaration((INamedTypeSymbol)x.TargetSymbol, x.TargetNode));
    }

    public static IncrementalValueProvider<ImmutableArray<AttributeData>> GetUseStaticDomainMapperDeclarations(
        IncrementalGeneratorInitializationContext context
    )
    {
        var staticDomainMapperAttributes = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                DomainMapGenerator.UseStaticDomainMapperName,
                static (s, _) => s is CompilationUnitSyntax,
                static (ctx, _) => ctx.Attributes
            )
            .SelectMany(static (x, _) => x)
            .Collect();
        var genericStaticDomainMapperAttributes = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                DomainMapGenerator.UseStaticDomainMapperGenericName,
                static (s, _) => s is CompilationUnitSyntax,
                static (ctx, _) => ctx.Attributes
            )
            .SelectMany(static (x, _) => x)
            .Collect();

        return staticDomainMapperAttributes
            .Combine(genericStaticDomainMapperAttributes)
            .SelectMany((x, _) => x.Left.AddRange(x.Right))
            .Collect();
    }

    public static IncrementalValueProvider<IAssemblySymbol?> GetMapperDefaultDeclarations(IncrementalGeneratorInitializationContext context)
    {
        return context
            .SyntaxProvider.ForAttributeWithMetadataName(
                DomainMapGenerator.MapperDefaultsAttributeName,
                static (s, _) => s is CompilationUnitSyntax,
                static (ctx, _) => (IAssemblySymbol)ctx.TargetSymbol
            )
            .Collect()
            .Select((x, _) => x.FirstOrDefault());
    }
}
