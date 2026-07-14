using DomainMap.Abstractions;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;

namespace DomainMap.Descriptors.ObjectFactories;

/// <summary>
/// An object factory represents a method to instantiate objects of a certain type.
/// </summary>
public abstract class ObjectFactory(
    SymbolAccessor symbolAccessor,
    IMethodSymbol method,
    bool mapToParameters = false,
    bool isDomainFactory = false
)
{
    internal IMethodSymbol Method { get; } = method;

    internal bool IsDomainFactory { get; } = isDomainFactory || symbolAccessor.HasAttribute<DomainFactoryAttribute>(method);

    internal bool MapToParameters { get; } = mapToParameters;

    public ExpressionSyntax CreateType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate, ExpressionSyntax source) =>
        HandleNull(BuildCreateType(sourceType, targetTypeToCreate, source), targetTypeToCreate);

    public ExpressionSyntax CreateType(
        ITypeSymbol sourceType,
        ITypeSymbol targetTypeToCreate,
        ExpressionSyntax source,
        IEnumerable<ArgumentSyntax> arguments
    ) => HandleNull(BuildCreateType(sourceType, targetTypeToCreate, source, arguments), targetTypeToCreate);

    public abstract bool CanCreateInstanceOfType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate);

    protected abstract ExpressionSyntax BuildCreateType(ITypeSymbol sourceType, ITypeSymbol targetTypeToCreate, ExpressionSyntax source);

    protected virtual ExpressionSyntax BuildCreateType(
        ITypeSymbol sourceType,
        ITypeSymbol targetTypeToCreate,
        ExpressionSyntax source,
        IEnumerable<ArgumentSyntax> arguments
    ) => BuildCreateType(sourceType, targetTypeToCreate, source);

    /// <summary>
    /// Wraps the <see cref="expression"/> in null handling.
    /// If the <see cref="expression"/> returns a nullable type, but the <see cref="typeToCreate"/> is not nullable,
    /// a new instance is created (if a parameterless ctor is accessible). Otherwise a <see cref="NullReferenceException"/> is thrown.
    /// If the <see cref="typeToCreate"/> is nullable, the <see cref="expression"/> is returned without additional handling.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="typeToCreate">The type to create.</param>
    /// <returns></returns>
    private ExpressionSyntax HandleNull(ExpressionSyntax expression, ITypeSymbol typeToCreate)
    {
        if (!Method.ReturnType.IsNullable())
            return expression;

        ExpressionSyntax nullFallback;
        if (IsDomainFactory)
        {
            nullFallback = ThrowNullReferenceException($"The domain factory {Method.Name} returned null");
        }
        else
        {
            nullFallback = symbolAccessor.HasDirectlyAccessibleParameterlessConstructor(typeToCreate)
                ? CreateInstance(typeToCreate)
                : ThrowNullReferenceException($"The object factory {Method.Name} returned null");
        }

        return Coalesce(expression, nullFallback);
    }
}
