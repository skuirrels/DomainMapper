using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    [SuppressMessage(
        "Maintainability",
        "MA0051",
        Justification = "Keeps closed-world registration validation and emitted dispatch ordering together."
    )]
    private void BuildRuntimeRegistry()
    {
        if (Attribute(_mapperType, MapRegistryAttribute) == null)
            return;

        if (HasVisibleMapperMember("TryMapRuntime") || HasVisibleMapperMember("MapRuntime"))
        {
            _diagnostics.Add(
                DiagnosticData.Create(
                    MapperDiagnostics.InvalidRegistry,
                    _mapperType.Locations.FirstOrDefault(),
                    _mapperType.Name,
                    "generated method names TryMapRuntime and MapRuntime must be available"
                )
            );
            return;
        }

        var candidates = _mappingMethods
            .Where(x =>
                x.IsStatic
                && !x.ReturnsVoid
                && x.TypeParameters.Length == 0
                && x.Parameters is [{ RefKind: RefKind.None }]
                && _successfulMappingMethods.Contains(x)
            )
            .ToArray();
        var duplicates = candidates
            .GroupBy(x => $"{RuntimeSourceTypeName(x.Parameters[0].Type)}->{RuntimeTypeName(x.ReturnType)}", StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .ToArray();
        if (duplicates.Length > 0)
        {
            foreach (var duplicate in duplicates)
            {
                _diagnostics.Add(
                    DiagnosticData.Create(
                        MapperDiagnostics.InvalidRegistry,
                        _mapperType.Locations.FirstOrDefault(),
                        _mapperType.Name,
                        $"mapping pair '{duplicate.Key}' is registered more than once"
                    )
                );
            }
            return;
        }

        var derivedCandidates = candidates.Where(x => HasAttribute(x, MapRegistryDerivedAttribute)).ToArray();
        var invalidDerivedCandidate = derivedCandidates.FirstOrDefault(x => !x.Parameters[0].Type.IsReferenceType);
        if (invalidDerivedCandidate != null)
        {
            _diagnostics.Add(
                DiagnosticData.Create(
                    MapperDiagnostics.InvalidRegistry,
                    invalidDerivedCandidate.Locations.FirstOrDefault(),
                    _mapperType.Name,
                    $"derived-source mapping '{invalidDerivedCandidate.Name}' requires a reference-type source"
                )
            );
            return;
        }
        for (var left = 0; left < derivedCandidates.Length; left++)
        {
            for (var right = left + 1; right < derivedCandidates.Length; right++)
            {
                var first = derivedCandidates[left];
                var second = derivedCandidates[right];
                if (!RuntimeTypesEqual(first.ReturnType, second.ReturnType))
                    continue;
                var overlaps = RuntimeSourceTypesMayOverlap(first.Parameters[0].Type, second.Parameters[0].Type);
                if (!overlaps)
                    continue;
                _diagnostics.Add(
                    DiagnosticData.Create(
                        MapperDiagnostics.InvalidRegistry,
                        _mapperType.Locations.FirstOrDefault(),
                        _mapperType.Name,
                        $"derived-source mappings '{first.Name}' and '{second.Name}' overlap for target '{second.ReturnType.ToDisplayString()}'"
                    )
                );
                return;
            }
        }

        var lines = new List<string>();
        foreach (
            var method in candidates.OrderBy(x => HasAttribute(x, MapRegistryDerivedAttribute)).ThenBy(x => x.Name, StringComparer.Ordinal)
        )
        {
            var sourceType = TypeName(method.Parameters[0].Type);
            var runtimeSourceType = RuntimeSourceTypeName(method.Parameters[0].Type);
            var runtimeTargetType = RuntimeTypeName(method.ReturnType);
            var sourceCheck = HasAttribute(method, MapRegistryDerivedAttribute)
                ? $"source is {runtimeSourceType} typedSource"
                : $"source.GetType() == typeof({runtimeSourceType})";
            var sourceArgument = HasAttribute(method, MapRegistryDerivedAttribute) ? "typedSource" : $"({sourceType})source";
            lines.Add(
                $"if ({sourceCheck} && targetType == typeof({runtimeTargetType}))\n{{\n    target = {Escape(method.Name)}({sourceArgument});\n    return true;\n}}"
            );
        }
        lines.Add("target = null;\nreturn false;");
        var visibility = _mapperType.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        _supportMembers.Add(
            GeneratedCodeAttribute
                + "\n"
                + $"{visibility} static bool TryMapRuntime(object source, global::System.Type targetType, out object? target)\n{{\n"
                + "    if (source is null) throw new global::System.ArgumentNullException(nameof(source));\n"
                + "    if (targetType is null) throw new global::System.ArgumentNullException(nameof(targetType));\n"
                + Indent(string.Join("\n", lines))
                + "\n}"
        );
        _supportMembers.Add(
            GeneratedCodeAttribute
                + "\n"
                + $"{visibility} static object? MapRuntime(object source, global::System.Type targetType)\n{{\n"
                + "    if (TryMapRuntime(source, targetType, out var target))\n        return target!;\n"
                + "    throw new global::System.InvalidOperationException(\"No DomainMapper mapping is registered from '\" + source.GetType() + \"' to '\" + targetType + \"'.\");\n}"
        );
    }

    private bool HasVisibleMapperMember(string name)
    {
        for (var current = _mapperType; current != null; current = current.BaseType)
        {
            if (
                current
                    .GetMembers(name)
                    .Any(x =>
                        SymbolEqualityComparer.Default.Equals(current, _mapperType) || x.DeclaredAccessibility != Accessibility.Private
                    )
            )
                return true;
        }
        return false;
    }

    private bool RuntimeSourceTypesMayOverlap(ITypeSymbol first, ITypeSymbol second)
    {
        if (_compilation.ClassifyConversion(first, second).IsImplicit || _compilation.ClassifyConversion(second, first).IsImplicit)
            return true;

        if (!first.IsReferenceType || !second.IsReferenceType)
            return false;
        if (first.TypeKind == TypeKind.Class && second.TypeKind == TypeKind.Class)
            return false;
        if (first is INamedTypeSymbol { IsSealed: true } || second is INamedTypeSymbol { IsSealed: true })
            return false;

        // Unrelated interfaces, or an open class/interface pair, can still be
        // implemented by the same runtime type even without a conversion
        // between the declared source types.
        return true;
    }
}
