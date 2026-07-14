using System.Diagnostics;
using DomainMap.Emit;
using DomainMap.Emit.Syntax;
using DomainMap.Helpers;
using DomainMap.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Descriptors.Mappings;

/// <summary>
/// Represents a mapping which is not a single expression but an entire method.
/// </summary>
[DebuggerDisplay("{GetType().Name}({SourceType} => {TargetType})")]
public abstract class MethodMapping : ITypeMapping, IParameterizedMapping
{
    protected const string DefaultReferenceHandlerParameterName = "refHandler";
    private const string DefaultSourceParameterName = "source";

    private const int SourceParameterIndex = 0;
    private const int ReferenceHandlerParameterIndex = 1;

    private static readonly IEnumerable<SyntaxToken> _privateSyntaxToken = [TrailingSpacedToken(SyntaxKind.PrivateKeyword)];

    private static readonly IEnumerable<SyntaxToken> _privateStaticSyntaxToken =
    [
        TrailingSpacedToken(SyntaxKind.PrivateKeyword),
        TrailingSpacedToken(SyntaxKind.StaticKeyword),
    ];

    private readonly ITypeSymbol _returnType;
    private readonly MethodDeclarationSyntax? _methodDeclarationSyntax;

    private string? _methodName;

    protected MethodMapping(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        TargetType = targetType;
        SourceParameter = new MethodParameter(SourceParameterIndex, DefaultSourceParameterName, sourceType);
        _returnType = targetType;
    }

    protected MethodMapping(
        IMethodSymbol method,
        MethodParameter sourceParameter,
        MethodParameter? referenceHandlerParameter,
        ITypeSymbol targetType
    )
    {
        TargetType = targetType;
        SourceParameter = sourceParameter;
        Method = method;
        IsExtensionMethod = method.IsExtensionMethod;
        ReferenceHandlerParameter = referenceHandlerParameter;
        _methodDeclarationSyntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
        _methodName = method.Name;
        _returnType = method.ReturnsVoid ? method.ReturnType : targetType;
    }

    public IReadOnlyCollection<MethodParameter> AdditionalSourceParameters { get; init; } = [];

    protected IMethodSymbol? Method { get; }

    protected bool IsExtensionMethod { get; }

    protected string MethodName => _methodName ?? throw new InvalidOperationException();

    protected MethodParameter SourceParameter { get; }

    protected MethodParameter? ReferenceHandlerParameter { get; private set; }

    public ITypeSymbol SourceType => SourceParameter.Type;

    public ITypeSymbol TargetType { get; }

    public bool IsSynthetic => false;

    public virtual IEnumerable<TypeMappingKey> BuildAdditionalMappingKeys(TypeMappingConfiguration config) => [];

    public virtual ExpressionSyntax Build(TypeMappingBuildContext ctx) =>
        ctx.SyntaxFactory.Invocation(MethodName, ctx.BuildArguments(Method, SourceParameter, ReferenceHandlerParameter));

    public virtual MethodDeclarationSyntax BuildMethod(SourceEmitterContext ctx)
    {
        IReadOnlyDictionary<string, ExpressionSyntax>? additionalParams = null;
        if (AdditionalSourceParameters.Count > 0)
        {
            additionalParams = AdditionalSourceParameters.ToDictionary(
                p => p.NormalizedName,
                p => (ExpressionSyntax)IdentifierName(p.Name),
                StringComparer.OrdinalIgnoreCase
            );
        }

        var typeMappingBuildContext = new TypeMappingBuildContext(
            SourceParameter.Name,
            ReferenceHandlerParameter?.Name,
            ctx.NameBuilder.NewScope(),
            ctx.SyntaxFactory,
            additionalParams
        );

        var parameters = BuildParameterList();
        ReserveParameterNames(typeMappingBuildContext.NameBuilder, parameters);

        var body = ctx.SyntaxFactory.Block(BuildBody(typeMappingBuildContext.AddIndentation()));
        var attributes = BuildAttributes(typeMappingBuildContext);
        if (ShouldAggressivelyInline(body))
            attributes = attributes.Add(typeMappingBuildContext.SyntaxFactory.AggressiveInliningAttribute());

        var returnType = FullyQualifiedIdentifier(_returnType);
        return MethodDeclaration(returnType.AddTrailingSpace(), Identifier(MethodName))
            .WithModifiers(TokenList(BuildModifiers(ctx.IsStatic)))
            .WithParameterList(parameters)
            .WithAttributeLists(attributes)
            .WithBody(body);
    }

    public abstract IEnumerable<StatementSyntax> BuildBody(TypeMappingBuildContext ctx);

    internal void SetMethodNameIfNeeded(Func<MethodMapping, string> methodNameBuilder)
    {
        _methodName ??= methodNameBuilder(this);
    }

    internal virtual void EnableReferenceHandling(INamedTypeSymbol iReferenceHandlerType)
    {
        ReferenceHandlerParameter ??= new MethodParameter(
            ReferenceHandlerParameterIndex,
            DefaultReferenceHandlerParameterName,
            iReferenceHandlerType
        );
    }

    protected internal virtual SyntaxList<AttributeListSyntax> BuildAttributes(TypeMappingBuildContext ctx) =>
        SingletonList(ctx.SyntaxFactory.GeneratedCodeAttribute());

    protected virtual ParameterListSyntax BuildParameterList() =>
        ParameterList(IsExtensionMethod, [SourceParameter, ReferenceHandlerParameter, .. AdditionalSourceParameters]);

    private IEnumerable<SyntaxToken> BuildModifiers(bool isStatic)
    {
        // if a syntax is referenced the code written by the user copy all modifiers,
        // otherwise only set private and optionally static
        if (_methodDeclarationSyntax != null)
        {
            return _methodDeclarationSyntax.Modifiers.Select(x => TrailingSpacedToken(x.Kind()));
        }

        return isStatic ? _privateStaticSyntaxToken : _privateSyntaxToken;
    }

    private void ReserveParameterNames(UniqueNameBuilder nameBuilder, ParameterListSyntax parameters)
    {
        foreach (var param in parameters.Parameters)
        {
            nameBuilder.Reserve(param.Identifier.Text);
        }
    }

    private bool ShouldAggressivelyInline(BlockSyntax body)
    {
        if (Method == null || SourceType.IsRefLikeType || TargetType.IsRefLikeType || body.Statements.Count > 6)
            return false;

        if (body.DescendantNodes().Any(x => x is InvocationExpressionSyntax or LambdaExpressionSyntax or QueryExpressionSyntax))
            return false;

        return !body.DescendantNodes()
            .Any(x =>
                x
                    is ForEachStatementSyntax
                        or ForStatementSyntax
                        or WhileStatementSyntax
                        or DoStatementSyntax
                        or IfStatementSyntax
                        or SwitchStatementSyntax
                        or TryStatementSyntax
                        or ThrowStatementSyntax
                        or ThrowExpressionSyntax
            );
    }
}
