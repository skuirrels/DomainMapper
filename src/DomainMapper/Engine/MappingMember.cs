using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed class MappingMember
{
    public MappingMember(ISymbol symbol, ITypeSymbol type, bool canRead, bool canWrite, bool isInitOnly, bool isRequired)
    {
        Symbol = symbol;
        Type = type;
        CanRead = canRead;
        CanWrite = canWrite;
        IsInitOnly = isInitOnly;
        IsRequired = isRequired;
    }

    public ISymbol Symbol { get; }

    public string Name => Symbol.Name;

    public ITypeSymbol Type { get; }

    public bool CanRead { get; }

    public bool CanWrite { get; }

    public bool IsInitOnly { get; }

    public bool IsRequired { get; }
}
