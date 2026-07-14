using DomainMap.Abstractions;
using DomainMap.Descriptors.Mappings;
using DomainMap.Diagnostics;
using DomainMap.Helpers;

namespace DomainMap.Descriptors.MappingBuilders;

/// <summary>
/// Reserves mappings configured with <see cref="MapToFactoryAttribute"/> for the required target-owned factory path.
/// This runs before ordinary construction and conversion builders so an explicit boundary can never be bypassed.
/// </summary>
public static class ConfiguredDomainFactoryMappingBuilder
{
    public static INewInstanceMapping? TryBuildMapping(MappingBuilderContext ctx)
    {
        if (ctx.UserSymbol == null || !ctx.SymbolAccessor.HasAttribute<MapToFactoryAttribute>(ctx.UserSymbol))
            return null;

        var configuration = ctx.AttributeAccessor.AccessFirstOrDefault<MapToFactoryAttribute>(ctx.UserSymbol)!;

        var sourceIsQueryable = ctx.Source.ImplementsGeneric(ctx.Types.Get(typeof(IQueryable<>)), out _);
        var targetIsQueryable = ctx.Target.ImplementsGeneric(ctx.Types.Get(typeof(IQueryable<>)), out var targetQueryable);
        var isQueryableProjection = sourceIsQueryable && targetIsQueryable;
        if (ctx.IsExpression || isQueryableProjection)
        {
            var targetType = isQueryableProjection ? targetQueryable!.TypeArguments[0] : ctx.Target;
            ctx.ReportDiagnosticAtSymbol(
                DiagnosticDescriptors.DomainFactoryCannotBeUsedInProjection,
                ctx.UserSymbol,
                configuration.FactoryMethodName,
                targetType
            );
            return new UnimplementedMapping(ctx.Source, ctx.Target);
        }

        return new NewInstanceObjectMemberMethodMapping(
            ctx.Source,
            ctx.Target.NonNullable(),
            ctx.Configuration.Mapper.UseReferenceHandling
        );
    }
}
