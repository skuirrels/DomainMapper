using DomainMap.Abstractions;
using DomainMap.Configuration;
using DomainMap.Configuration.MethodReferences;
using DomainMap.Descriptors.Mappings.UserMappings;
using DomainMap.Diagnostics;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.ExternalMappings;

internal static class ExternalMappingsExtractor
{
    public static IEnumerable<IUserMapping> ExtractExternalMappings(
        IEnumerable<UseStaticDomainMapperConfiguration> assemblyScopedStaticMappers,
        SimpleMappingBuilderContext ctx,
        INamedTypeSymbol mapperSymbol
    )
    {
        return ExtractExternalStaticMappings(assemblyScopedStaticMappers, ctx)
            .Concat(ExtractExternalStaticMappings(ExtractStaticMappersFromAttributes(ctx, mapperSymbol), ctx))
            .Concat(ExtractExternalInstanceMappings(ctx, mapperSymbol));
    }

    private static IEnumerable<UseStaticDomainMapperConfiguration> ExtractStaticMappersFromAttributes(
        SimpleMappingBuilderContext ctx,
        INamedTypeSymbol mapperSymbol
    )
    {
        return ctx
            .AttributeAccessor.Access<UseStaticDomainMapperAttribute, UseStaticDomainMapperConfiguration>(mapperSymbol)
            .Concat(ctx.AttributeAccessor.Access<UseStaticDomainMapperAttribute<object>, UseStaticDomainMapperConfiguration>(mapperSymbol));
    }

    private static IEnumerable<IUserMapping> ExtractExternalStaticMappings(
        IEnumerable<UseStaticDomainMapperConfiguration> staticMappers,
        SimpleMappingBuilderContext ctx
    )
    {
        var staticExternalMappers = staticMappers.SelectMany(x =>
            UserMethodMappingExtractor.ExtractUserImplementedMappings(
                ctx,
                x.MapperType,
                receiver: x.MapperType.FullyQualifiedIdentifierName(),
                isStatic: true,
                isExternal: true
            )
        );
        return staticExternalMappers;
    }

    private static IEnumerable<IUserMapping> ExtractExternalInstanceMappings(SimpleMappingBuilderContext ctx, INamedTypeSymbol mapperSymbol)
    {
        return ctx
            .SymbolAccessor.GetAllMembers(mapperSymbol)
            .Where(x => ctx.AttributeAccessor.HasAttribute<UseDomainMapperAttribute>(x))
            .SelectMany(x => ValidateAndExtractExternalInstanceMappings(ctx, x));
    }

    public static IEnumerable<(string Name, IUserMapping Mapping)> ExtractExternalNamedMappings(
        SimpleMappingBuilderContext ctx,
        INamedTypeSymbol mapperSymbol
    )
    {
        return ctx
            .SymbolAccessor.GetAllMethods(mapperSymbol)
            .SelectMany(CollectMemberMappingConfigurations)
            .SelectMany(e => UserMethodMappingExtractor.ExtractNamedUserImplementedMappings(ctx, e).Select(y => (e.FullName, y)));

        IEnumerable<IMethodReferenceConfiguration> CollectMemberMappingConfigurations(IMethodSymbol x) =>
            ctx
                .AttributeAccessor.Access<MapPropertyAttribute, MemberMappingConfiguration>(x)
                .Select(e => e.Use)
                .Concat(ctx.AttributeAccessor.Access<MapPropertyFromSourceAttribute, MemberMappingConfiguration>(x).Select(e => e.Use))
                .Concat(
                    ctx.AttributeAccessor.Access<IncludeMappingConfigurationAttribute, IncludeMappingConfiguration>(x).Select(e => e.Name)
                )
                .Where(e => e?.IsExternal ?? false)
                .WhereNotNull();
    }

    private static IEnumerable<IUserMapping> ValidateAndExtractExternalInstanceMappings(SimpleMappingBuilderContext ctx, ISymbol symbol)
    {
        var (name, type, nullableAnnotation) = symbol switch
        {
            IFieldSymbol field => (field.Name, field.Type, field.NullableAnnotation),
            IPropertySymbol prop => (prop.Name, prop.Type, prop.NullableAnnotation),
            _ => (string.Empty, null, NullableAnnotation.None),
        };

        if (type == null)
            return [];

        if (nullableAnnotation != NullableAnnotation.Annotated)
            return UserMethodMappingExtractor.ExtractUserImplementedMappings(ctx, type, name, isStatic: false, isExternal: true);

        ctx.ReportDiagnostic(DiagnosticDescriptors.ExternalMapperMemberCannotBeNullable, symbol, symbol.ToDisplayString());
        return [];
    }
}
