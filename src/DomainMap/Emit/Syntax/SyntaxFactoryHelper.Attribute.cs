using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Emit.Syntax;

public partial struct SyntaxFactoryHelper
{
    private const int CachedAttributeIndentationCount = 8;
    private const string UnsafeAccessorName = "global::System.Runtime.CompilerServices.UnsafeAccessor";
    private const string UnsafeAccessorKindName = "global::System.Runtime.CompilerServices.UnsafeAccessorKind";
    private const string UnsafeAccessorNameArgument = "Name";
    private const string MethodImplName = "global::System.Runtime.CompilerServices.MethodImpl";
    private const string MethodImplOptionsName = "global::System.Runtime.CompilerServices.MethodImplOptions";

    private static readonly IdentifierNameSyntax _unsafeAccessorKindName = IdentifierName(UnsafeAccessorKindName);

    private static class AttributeCache
    {
        public static readonly AttributeListSyntax[] AggressiveInlining = Enumerable
            .Range(0, CachedAttributeIndentationCount)
            .Select(BuildAggressiveInliningAttribute)
            .ToArray();

        public static readonly AttributeListSyntax[] GeneratedCode = Enumerable
            .Range(0, CachedAttributeIndentationCount)
            .Select(BuildGeneratedCodeAttribute)
            .ToArray();
    }

    public AttributeListSyntax UnsafeAccessorAttribute(UnsafeAccessorType type, string? name = null)
    {
        var unsafeAccessType = type switch
        {
            UnsafeAccessorType.Field => "Field",
            UnsafeAccessorType.Method => "Method",
            UnsafeAccessorType.Constructor => "Constructor",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown {nameof(UnsafeAccessorType)}"),
        };

        var kind = MemberAccess(_unsafeAccessorKindName, IdentifierName(unsafeAccessType));
        if (name == null)
            return Attribute(UnsafeAccessorName, kind);

        return Attribute(UnsafeAccessorName, kind, Assignment(IdentifierName(UnsafeAccessorNameArgument), StringLiteral(name)));
    }

    public AttributeListSyntax AggressiveInliningAttribute() =>
        Indentation < CachedAttributeIndentationCount
            ? AttributeCache.AggressiveInlining[Indentation]
            : BuildAggressiveInliningAttribute(Indentation);

    private static AttributeListSyntax BuildAggressiveInliningAttribute(int indentation)
    {
        var syntaxFactory = new SyntaxFactoryHelper(indentation, default);
        return syntaxFactory.Attribute(
            MethodImplName,
            MemberAccess(IdentifierName(MethodImplOptionsName), IdentifierName("AggressiveInlining"))
        );
    }

    private AttributeListSyntax Attribute(string name, params ExpressionSyntax[] arguments)
    {
        var args = CommaSeparatedList(arguments.Select(AttributeArgument));
        var attribute = SyntaxFactory.Attribute(IdentifierName(name));
        if (args.Count > 0)
        {
            attribute = attribute.WithArgumentList(AttributeArgumentList(args));
        }

        return AttributeList(SingletonSeparatedList(attribute)).AddTrailingLineFeed(Indentation);
    }

    public enum UnsafeAccessorType
    {
        Method,
        Field,
        Constructor,
    }
}
