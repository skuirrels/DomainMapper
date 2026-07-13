using DomainMap.Abstractions;
using DomainMap.Configuration;
using DomainMap.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors;

internal static class MapperIgnoreHelper
{
    public static bool CheckIgnored(ISymbol symbol, string ignoredName, SimpleMappingBuilderContext ctx)
    {
        var ignoreConfiguration = ctx.AttributeAccessor.AccessFirstOrDefault<MapperIgnoreAttribute, MapperIgnoreConfiguration>(symbol);
        if (ignoreConfiguration == null)
            return false;

        if (string.IsNullOrWhiteSpace(ignoreConfiguration.Justification))
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.IgnoreMissingJustification, ignoreConfiguration.Location, ignoredName);
        }

        return true;
    }
}
