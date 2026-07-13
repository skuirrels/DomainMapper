using DomainMap.Helpers;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.Mappings;

internal static class NullFallbackValueExtensions
{
    public static bool IsNullable(this NullFallbackValue fallbackValue, ITypeSymbol targetType) =>
        fallbackValue == NullFallbackValue.Default && targetType.IsNullable();
}
