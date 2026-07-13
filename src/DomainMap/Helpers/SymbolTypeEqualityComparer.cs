using Microsoft.CodeAnalysis;

namespace DomainMap.Helpers;

internal static class SymbolTypeEqualityComparer
{
    public static readonly IEqualityComparer<IFieldSymbol?> FieldDefault = SymbolEqualityComparer.Default;
    public static readonly IEqualityComparer<IMethodSymbol?> MethodDefault = SymbolEqualityComparer.Default;
}
