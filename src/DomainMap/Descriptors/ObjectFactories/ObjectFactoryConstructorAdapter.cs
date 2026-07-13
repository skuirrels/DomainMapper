using DomainMap.Descriptors.Constructors;
using DomainMap.Descriptors.Mappings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.ObjectFactories;

/// <summary>
/// An adapter to adapt a general <see cref="ObjectFactory"/> as a <see cref="IInstanceConstructor"/>
/// for a concrete type pair.
/// </summary>
public class ObjectFactoryConstructorAdapter(ObjectFactory objectFactory, ITypeSymbol sourceType, ITypeSymbol targetType)
    : IParameterMappingInstanceConstructor
{
    public bool IsDomainFactory => objectFactory.IsDomainFactory;

    public bool SupportsObjectInitializer => false;

    public bool SupportsMemberAssignment => !objectFactory.IsDomainFactory;

    public bool SupportsParameterMapping => objectFactory.MapToParameters;

    public IMethodSymbol ParameterMappingMethod => objectFactory.Method;

    public ExpressionSyntax CreateInstance(
        TypeMappingBuildContext ctx,
        IEnumerable<ArgumentSyntax> args,
        InitializerExpressionSyntax? initializer = null
    )
    {
        return objectFactory.MapToParameters
            ? objectFactory.CreateType(sourceType, targetType, ctx.Source, args)
            : objectFactory.CreateType(sourceType, targetType, ctx.Source);
    }
}
