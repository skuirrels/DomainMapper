using DomainMap.Descriptors.Constructors;
using DomainMap.Descriptors.Mappings;
using DomainMap.Emit;
using DomainMap.Emit.Syntax;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Descriptors.UnsafeAccess;

/// <summary>
/// Creates an extension method to create an instance using a non-public ctor .Net 8's UnsafeAccessor.
/// <code>
/// [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
/// public extern static global::MyClass Create();
/// </code>
/// </summary>
/// <param name="symbol">The symbol of the ctor.</param>
/// <param name="className">The name of the accessor class.</param>
/// <param name="methodName">The name of the accessor method.</param>
public class UnsafeConstructorAccessor(IMethodSymbol symbol, string className, string methodName) : IUnsafeAccessor, IInstanceConstructor
{
    public bool SupportsObjectInitializer => false;

    public bool SupportsMemberAssignment => true;

    public MethodDeclarationSyntax BuildAccessorMethod(SourceEmitterContext ctx)
    {
        var methodSymbol = symbol.ContainingType.IsGenericType ? symbol.OriginalDefinition : symbol;
        var typeToCreate = IdentifierName(methodSymbol.ContainingType.FullyQualifiedIdentifierName()).AddTrailingSpace();
        var parameters = ParameterList(methodSymbol.Parameters);
        var attribute = ctx.SyntaxFactory.UnsafeAccessorAttribute(UnsafeAccessorType.Constructor);
        return ctx.SyntaxFactory.PublicStaticExternMethod(typeToCreate, methodName, parameters, [attribute]);
    }

    public ExpressionSyntax CreateInstance(
        TypeMappingBuildContext ctx,
        IEnumerable<ArgumentSyntax> args,
        InitializerExpressionSyntax? initializer = null
    )
    {
        if (!symbol.ContainingType.IsGenericType)
        {
            return ctx.SyntaxFactory.StaticInvocation(className, methodName, args);
        }

        var genericClassName = GenericName(className).WithTypeArgumentList(TypeArgumentList(symbol.ContainingType.TypeArguments));
        return InvocationExpression(MemberAccess(genericClassName, methodName)).WithArgumentList(ArgumentListWithoutIndention(args));
    }
}
