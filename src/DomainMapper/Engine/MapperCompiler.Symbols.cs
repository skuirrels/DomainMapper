using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    private IReadOnlyList<MappingMember> ReadableMembers(ITypeSymbol type) =>
        GetConventionMappingMembers(type).Where(x => x.CanRead).ToArray();

    private IReadOnlyList<MappingMember> AllReadableMembers(ITypeSymbol type) => GetAllMappingMembers(type).Where(x => x.CanRead).ToArray();

    private IReadOnlyList<MappingMember> WritableMembers(ITypeSymbol type) =>
        GetConventionMappingMembers(type).Where(x => x.CanWrite && !x.IsInitOnly).ToArray();

    private IReadOnlyList<MappingMember> SettableMembers(ITypeSymbol type) =>
        GetConventionMappingMembers(type).Where(x => x.CanWrite).ToArray();

    private IReadOnlyList<MappingMember> ReadableTargetMembers(ITypeSymbol type, MappingMethodConfiguration? configuration) =>
        GetTargetMappingMembers(type, configuration).Where(x => x.CanRead).ToArray();

    private IReadOnlyList<MappingMember> WritableTargetMembers(ITypeSymbol type, MappingMethodConfiguration? configuration) =>
        GetTargetMappingMembers(type, configuration).Where(x => x.CanWrite && !x.IsInitOnly).ToArray();

    private IReadOnlyList<MappingMember> SettableTargetMembers(ITypeSymbol type, MappingMethodConfiguration? configuration) =>
        GetTargetMappingMembers(type, configuration).Where(x => x.CanWrite).ToArray();

    private IEnumerable<MappingMember> GetConventionMappingMembers(ITypeSymbol type) =>
        GetAllMappingMembers(type).Where(x => x.Symbol is not IFieldSymbol);

    private IEnumerable<MappingMember> GetTargetMappingMembers(ITypeSymbol type, MappingMethodConfiguration? configuration) =>
        GetAllMappingMembers(type).Where(x => x.Symbol is not IFieldSymbol || IsExplicitTargetMember(configuration, x.Name));

    private static bool IsExplicitTargetMember(MappingMethodConfiguration? configuration, string memberName) =>
        configuration != null
        && (
            configuration.Bindings.ContainsKey(memberName)
            || configuration.ComputedMembers.ContainsKey(memberName)
            || configuration.Conditions.ContainsKey(memberName)
            || configuration.NullBehaviors.ContainsKey(memberName)
            || configuration.NullSubstitutes.ContainsKey(memberName)
            || configuration.CollectionPolicies.ContainsKey(memberName)
            || configuration.OnlyTargets?.Contains(memberName) == true
        );

    private IReadOnlyList<MappingMember> GetAllMappingMembers(ITypeSymbol type)
    {
        if (_mappingMembers.TryGetValue(type, out var cachedMembers))
            return cachedMembers;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (type is not INamedTypeSymbol named)
            return [];

        var members = new List<MappingMember>();
        for (var current = named; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (seen.Add(property.Name))
                {
                    members.Add(
                        new MappingMember(
                            property,
                            property.Type,
                            !property.IsStatic && !property.IsIndexer && property.GetMethod != null && IsAccessible(property.GetMethod),
                            !property.IsStatic && !property.IsIndexer && property.SetMethod != null && IsAccessible(property.SetMethod),
                            property.SetMethod?.IsInitOnly == true,
                            property.IsRequired
                        )
                    );
                }
            }

            foreach (var field in current.GetMembers().OfType<IFieldSymbol>())
            {
                if (seen.Add(field.Name))
                {
                    members.Add(
                        new MappingMember(
                            field,
                            field.Type,
                            !field.IsStatic && IsAccessible(field),
                            !field.IsStatic && !field.IsReadOnly && !field.IsConst && IsAccessible(field),
                            false,
                            field.IsRequired
                        )
                    );
                }
            }
        }

        if (named.TypeKind == TypeKind.Interface)
        {
            foreach (var interfaceType in named.AllInterfaces)
            {
                foreach (var property in interfaceType.GetMembers().OfType<IPropertySymbol>())
                {
                    if (seen.Add(property.Name))
                    {
                        members.Add(
                            new MappingMember(
                                property,
                                property.Type,
                                !property.IsStatic && !property.IsIndexer && property.GetMethod != null && IsAccessible(property.GetMethod),
                                !property.IsStatic && !property.IsIndexer && property.SetMethod != null && IsAccessible(property.SetMethod),
                                property.SetMethod?.IsInitOnly == true,
                                property.IsRequired
                            )
                        );
                    }
                }
            }
        }

        _mappingMembers.Add(type, members);
        return members;
    }

    private static bool TryFindMember(IReadOnlyList<MappingMember> members, string name, out MappingMember member)
    {
        var exact = members.Where(x => string.Equals(x.Name, name, StringComparison.Ordinal)).ToArray();
        if (exact.Length == 1)
        {
            member = exact[0];
            return true;
        }

        var insensitive = members.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (insensitive.Length == 1)
        {
            member = insensitive[0];
            return true;
        }

        member = null!;
        return false;
    }

    private static bool TryFindValue(IReadOnlyList<MappingValue> values, string name, out MappingValue value)
    {
        var exact = values.Where(x => string.Equals(x.Name, name, StringComparison.Ordinal)).ToArray();
        if (exact.Length == 1)
        {
            value = exact[0];
            return true;
        }

        var insensitive = values.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (insensitive.Length == 1)
        {
            value = insensitive[0];
            return true;
        }

        value = null!;
        return false;
    }

    private static bool NamesEqual(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private bool IsAccessible(ISymbol symbol) => _compilation.IsSymbolAccessibleWithin(symbol, _mapperType);

    private static bool IsRecordCopyConstructor(IMethodSymbol constructor, INamedTypeSymbol targetType) =>
        targetType.IsRecord && constructor.Parameters is [{ Type: var parameterType }] && TypesEqual(parameterType, targetType);

    private IMethodSymbol? FindSingleValueConstructor(ITypeSymbol sourceType, INamedTypeSymbol targetType) =>
        targetType
            .InstanceConstructors.Where(IsAccessible)
            .Where(x => !IsRecordCopyConstructor(x, targetType))
            .FirstOrDefault(x =>
                x.Parameters is [{ RefKind: RefKind.None } parameter]
                && _compilation.ClassifyConversion(sourceType, parameter.Type) is { Exists: true, IsImplicit: true }
            );

    private bool CanUseScalarConstructor(INamedTypeSymbol targetType, IMethodSymbol constructor, ISet<string> consumedMembers)
    {
        if (!SetsRequiredMembers(constructor) && RequiredFields(targetType).Count > 0)
            return false;

        return SettableMembers(targetType)
            .All(x => consumedMembers.Contains(x.Name) && (!x.IsRequired || SetsRequiredMembers(constructor)));
    }

    private bool SetsRequiredMembers(IMethodSymbol constructor) => Attribute(constructor, SetsRequiredMembersAttribute) != null;

    private static IReadOnlyList<IFieldSymbol> RequiredFields(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return [];

        var fields = new List<IFieldSymbol>();
        for (var current = named; current != null; current = current.BaseType)
        {
            fields.AddRange(current.GetMembers().OfType<IFieldSymbol>().Where(x => !x.IsStatic && x.IsRequired));
        }
        return fields;
    }

    private static IEnumerable<IMethodSymbol> GetAllMethods(INamedTypeSymbol type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(name).OfType<IMethodSymbol>())
            {
                yield return method;
            }
        }
    }

    private static bool TypesEqual(ITypeSymbol left, ITypeSymbol right) => SymbolEqualityComparer.IncludeNullability.Equals(left, right);

    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(TypeDisplayFormat);

    private static string RuntimeTypeName(ITypeSymbol type) => TypeName(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

    private static bool RuntimeTypesEqual(ITypeSymbol first, ITypeSymbol second) =>
        SymbolEqualityComparer.Default.Equals(
            first.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
            second.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
        );

    private static string RuntimeSourceTypeName(ITypeSymbol type) =>
        RuntimeTypeName(
            type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0]
                : type
        );
}
