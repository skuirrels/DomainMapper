using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed class MappingRequest
{
    public MappingRequest(ITypeSymbol sourceType, ITypeSymbol targetType, string methodName, MappingContext context)
    {
        SourceType = sourceType;
        TargetType = targetType;
        MethodName = methodName;
        Context = context;
    }

    public ITypeSymbol SourceType { get; }

    public ITypeSymbol TargetType { get; }

    public string MethodName { get; }

    public MappingContext Context { get; }
}
