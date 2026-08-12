using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed class MappingRequest
{
    public MappingRequest(ITypeSymbol sourceType, ITypeSymbol targetType, string methodName)
    {
        SourceType = sourceType;
        TargetType = targetType;
        MethodName = methodName;
    }

    public ITypeSymbol SourceType { get; }

    public ITypeSymbol TargetType { get; }

    public string MethodName { get; }
}
