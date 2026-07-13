using DomainMap.Symbols.Members;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;

namespace DomainMap.Descriptors.Enumerables.Capacity;

/// <summary>
/// Ensures the capacity of a collection by calling `EnsureCapacity(int)`
/// </summary>
internal sealed class EnsureCapacityMethodSetter : IMemberSetter
{
    public static readonly EnsureCapacityMethodSetter Instance = new();

    public const string EnsureCapacityMethodName = "EnsureCapacity";

    private EnsureCapacityMethodSetter() { }

    public bool SupportsCoalesceAssignment => false;

    public ExpressionSyntax BuildAssignment(
        ExpressionSyntax? baseAccess,
        ExpressionSyntax valueToAssign,
        INamedTypeSymbol? containingType = null,
        bool coalesceAssignment = false
    )
    {
        if (baseAccess == null)
            throw new ArgumentNullException(nameof(baseAccess));

        return InvocationWithoutIndention(MemberAccess(baseAccess, EnsureCapacityMethodName), valueToAssign);
    }
}
