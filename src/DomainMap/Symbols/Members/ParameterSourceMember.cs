using System.Diagnostics;
using DomainMap.Descriptors;
using DomainMap.Descriptors.UnsafeAccess;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Symbols.Members;

/// <summary>
/// A mapping method parameter represented as a mappable member.
/// This is semantically not really a member, but it acts as an additional mapping source member
/// and is therefore in terms of the mapping the same.
/// </summary>
[DebuggerDisplay("{Name}")]
public class ParameterSourceMember(MethodParameter parameter, SymbolAccessor symbolAccessor) : IMappableMember, IMemberGetter
{
    public string Name => parameter.Name;
    public ITypeSymbol Type => parameter.Type;
    public INamedTypeSymbol? ContainingType => null;
    public bool IsReadNullable =>
        parameter.Symbol is not null ? symbolAccessor.IsReadNullable(parameter.Symbol) : parameter.Type.IsNullable();
    public bool IsWriteNullable =>
        parameter.Symbol is not null ? symbolAccessor.IsWriteNullable(parameter.Symbol) : parameter.Type.IsNullable();
    public bool CanGet => true;
    public bool CanGetDirectly => true;
    public bool CanSet => false;
    public bool CanSetDirectly => false;
    public bool IsInitOnly => false;
    public bool IsRequired => false;
    public bool IsObsolete => false;

    public bool IsIgnored(MappingBuilderContext ctx) => false;

    public IMemberGetter BuildGetter(UnsafeAccessorContext ctx) => this;

    public IMemberSetter BuildSetter(UnsafeAccessorContext ctx) =>
        throw new InvalidOperationException($"Cannot create a setter for {nameof(ParameterSourceMember)}");

    public ExpressionSyntax BuildAccess(
        ExpressionSyntax? baseAccess,
        INamedTypeSymbol? containingType = null,
        bool nullConditional = false
    ) => IdentifierName(Name);

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
            return false;

        if (ReferenceEquals(this, obj))
            return true;

        if (obj.GetType() != GetType())
            return false;

        var other = (ParameterSourceMember)obj;
        return string.Equals(Name, other.Name, StringComparison.Ordinal)
            && SymbolEqualityComparer.IncludeNullability.Equals(Type, other.Type);
    }

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);
}
