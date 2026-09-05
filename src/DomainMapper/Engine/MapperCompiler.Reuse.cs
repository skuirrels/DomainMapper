using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    /// <summary>
    /// Resolves the declared mapping method that owns a nested source/target pair so nested values honour that method's
    /// contract, including <c>[MapToFactory]</c>, instead of a convention helper. Returns null when no single declared
    /// method applies. <paramref name="ambiguous"/> reports more than one candidate; callers treat that as a failed conversion.
    /// Reuse never applies to the mapping's own root pair, and never inside bounded-recursion or reference-tracking
    /// contexts because those thread depth and tracker state through generated helpers.
    /// </summary>
    private IMethodSymbol? ResolveDeclaredMapping(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        MappingContext context,
        bool record,
        out bool ambiguous
    )
    {
        ambiguous = false;
        var configuration = context.Configuration;
        if (configuration == null || configuration.PreserveReferences || configuration.MaximumDepth != null)
            return null;
        if (RuntimeTypesEqual(configuration.SourceType, sourceType) && RuntimeTypesEqual(configuration.TargetType, targetType))
            return null;

        var candidates = _mappingMethods.Where(x => IsReusableMapping(x, sourceType, targetType)).ToArray();
        if (candidates.Length == 0)
            return null;
        if (candidates.Length > 1)
        {
            ambiguous = true;
            var key = $"{configuration.Method.ToDisplayString()}|{TypeName(sourceType)}->{TypeName(targetType)}";
            if (record && _reportedAmbiguousReuse.Add(key))
            {
                ReportInvalidConfiguration(
                    configuration.Method,
                    $"nested value '{sourceType.ToDisplayString()}' to '{targetType.ToDisplayString()}' matches more than one declared mapping method "
                        + $"({string.Join(", ", candidates.Select(x => x.Name))}); declare a [DomainFactory] to select one"
                );
            }
            return null;
        }

        if (record)
        {
            if (!_declaredMappingReuse.TryGetValue(configuration.Method, out var reused))
            {
                reused = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                _declaredMappingReuse.Add(configuration.Method, reused);
            }
            reused.Add(candidates[0]);
        }
        return candidates[0];
    }

    private static bool IsReusableMapping(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol targetType) =>
        method.IsStatic
        && !method.ReturnsVoid
        && method.TypeParameters.Length == 0
        && method.Parameters is [{ RefKind: RefKind.None } parameter]
        && TypesEqual(parameter.Type, sourceType)
        && TypesEqual(method.ReturnType, targetType);

    /// <summary>
    /// Declared mappings that reuse each other in a cycle would recurse without a depth guard, so they are rejected the same
    /// way convention recursion is. <c>[MapMaxDepth]</c> on one method disables reuse there and breaks the cycle.
    /// </summary>
    private void RejectDeclaredMappingCycles()
    {
        foreach (var method in _declaredMappingReuse.Keys.Where(ReachesItself).ToArray())
        {
            var declaration = BuildDeclaration(method);
            _rootContracts.RemoveAll(x => string.Equals(x.Declaration, declaration, StringComparison.Ordinal));
            _successfulMappingMethods.Remove(method);
            ReportInvalidConfiguration(
                method,
                "declared mapping methods reuse each other in a cycle; opt into bounded recursion with [MapMaxDepth] on one of them"
            );
        }
    }

    private bool ReachesItself(IMethodSymbol method)
    {
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IMethodSymbol>(_declaredMappingReuse[method]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (SymbolEqualityComparer.Default.Equals(current, method))
                return true;
            if (!visited.Add(current) || !_declaredMappingReuse.TryGetValue(current, out var next))
                continue;
            foreach (var reused in next)
                pending.Push(reused);
        }
        return false;
    }
}
