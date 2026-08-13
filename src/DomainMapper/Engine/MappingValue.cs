using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed class MappingValue
{
    public MappingValue(string name, ITypeSymbol type, string expression)
    {
        Name = name;
        Type = type;
        Expression = expression;
    }

    public string Name { get; }

    public ITypeSymbol Type { get; }

    public string Expression { get; }
}
