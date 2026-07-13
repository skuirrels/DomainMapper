using DomainMap.Descriptors;
using DomainMap.Descriptors.UnsafeAccess;
using Microsoft.CodeAnalysis;

namespace DomainMap.Symbols.Members;

/// <summary>
/// A mappable member is a member of a class which can take part in a mapping.
/// (e.g., a field or a property).
/// </summary>
public interface IMappableMember
{
    string Name { get; }

    ITypeSymbol Type { get; }

    INamedTypeSymbol? ContainingType { get; }

    bool IsReadNullable { get; }

    bool IsWriteNullable { get; }

    /// <summary>
    /// Whether the member can be read using direct access or an unsafe accessor method.
    /// </summary>
    bool CanGet { get; }

    /// <summary>
    /// Whether the member can be read using simple assignment.
    /// </summary>
    bool CanGetDirectly { get; }

    /// <summary>
    /// Whether the member can be modified using an assignment or an unsafe accessor method.
    /// </summary>
    bool CanSet { get; }

    /// <summary>
    /// Whether the member can be modified using simple assignment.
    /// </summary>
    bool CanSetDirectly { get; }

    bool IsInitOnly { get; }

    bool IsRequired { get; }

    bool IsObsolete { get; }

    bool IsIgnored(MappingBuilderContext ctx);

    IMemberGetter BuildGetter(UnsafeAccessorContext ctx);
    IMemberSetter BuildSetter(UnsafeAccessorContext ctx);
}
