using DomainMap.Descriptors.Mappings;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.MappingBuilders;

public static class DirectAssignmentMappingBuilder
{
    public static NewInstanceMapping? TryBuildMapping(MappingBuilderContext ctx)
    {
        return
            SymbolEqualityComparer.IncludeNullability.Equals(ctx.Source, ctx.Target)
            && (!ctx.Configuration.UseDeepCloning || ctx.Source.IsImmutable())
            ? new DirectAssignmentMapping(ctx.Source)
            : null;
    }
}
