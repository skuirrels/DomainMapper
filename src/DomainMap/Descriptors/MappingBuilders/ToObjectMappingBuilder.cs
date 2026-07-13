using DomainMap.Abstractions;
using DomainMap.Descriptors.Mappings;
using DomainMap.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.MappingBuilders;

public static class ToObjectMappingBuilder
{
    public static NewInstanceMapping? TryBuildMapping(MappingBuilderContext ctx)
    {
        if (!ctx.IsConversionEnabled(MappingConversionType.ExplicitCast))
            return null;

        if (ctx.Target.SpecialType != SpecialType.System_Object)
            return null;

        if (!ctx.Configuration.UseDeepCloning)
            return new CastMapping(ctx.Source, ctx.Target);

        if (ctx.Source.SpecialType == SpecialType.System_Object)
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.MappedObjectToObjectWithoutDeepClone, ctx.Source.Name, ctx.Target.Name);
            return new DirectAssignmentMapping(ctx.Source);
        }

        return new CastMapping(ctx.Source, ctx.Target, ctx.FindOrBuildMapping(ctx.Source, ctx.Source));
    }
}
