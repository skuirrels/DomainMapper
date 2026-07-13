using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.FormatProviders;

public record FormatProvider(string Name, bool Default, ISymbol Symbol);
