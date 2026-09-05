using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    /// <summary>
    /// Maps one enum to another by member name through a generated switch with a throwing default. Every source member
    /// must have a same-named target member; flags enums and aliased source values are rejected because a value switch
    /// cannot represent them. A <c>[DomainFactory]</c> for the pair takes precedence and is the escape hatch.
    /// </summary>
    private string? BuildEnumConversion(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression, MappingContext context)
    {
        if (!TryMatchEnumMembers(sourceType, targetType, out var pairs, out var reason))
        {
            ISymbol reportedOn = (ISymbol?)context.Configuration?.Method ?? _mapperType;
            if (_reportedEnumMismatches.Add($"{reportedOn.ToDisplayString()}|{TypeName(sourceType)}->{TypeName(targetType)}"))
            {
                _diagnostics.Add(
                    DiagnosticData.Create(
                        MapperDiagnostics.EnumNotMapped,
                        reportedOn.Locations.FirstOrDefault(),
                        reportedOn.Name,
                        sourceType.ToDisplayString(),
                        targetType.ToDisplayString(),
                        reason
                    )
                );
            }
            return null;
        }

        var key = $"enum|{TypeName(sourceType)}->{TypeName(targetType)}";
        if (ReserveHelper(key, $"MapTo{Sanitize(targetType.Name)}", out var helperName))
        {
            var arms = pairs.Select(x => $"    {TypeName(sourceType)}.{Escape(x.Source)} => {TypeName(targetType)}.{Escape(x.Target)},");
            var fallback =
                $"    _ => throw new global::System.InvalidOperationException($\"Enum value '{{source}}' of '{sourceType.ToDisplayString()}' cannot be mapped to '{targetType.ToDisplayString()}'.\"),";
            var body = "return source switch\n{\n" + string.Join("\n", arms) + "\n" + fallback + "\n};";
            _helperContracts.Add(
                new MappingContract(
                    helperName,
                    $"private static {TypeName(targetType)} {Escape(helperName)}({TypeName(sourceType)} source)",
                    body,
                    MappingShape.Helper
                )
            );
        }
        return $"{Escape(helperName)}({sourceExpression})";
    }

    private static bool TryMatchEnumMembers(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        out ImmutableArray<(string Source, string Target)> pairs,
        out string reason
    )
    {
        pairs = ImmutableArray<(string Source, string Target)>.Empty;
        reason = string.Empty;
        if (HasFlagsAttribute(sourceType) || HasFlagsAttribute(targetType))
        {
            reason = "flags enums combine values; declare a [DomainFactory] for the pair";
            return false;
        }

        var sourceMembers = EnumMembers(sourceType);
        var aliased = sourceMembers
            .GroupBy(x => x.ConstantValue)
            .Where(x => x.Count() > 1)
            .SelectMany(x => x.Select(y => y.Name))
            .ToArray();
        if (aliased.Length > 0)
        {
            reason = $"source members {Quote(aliased)} share a value and cannot be distinguished; declare a [DomainFactory] for the pair";
            return false;
        }

        var targetMembers = EnumMembers(targetType).Select(x => x.Name).ToArray();
        var builder = ImmutableArray.CreateBuilder<(string Source, string Target)>();
        var missing = new List<string>();
        foreach (var member in sourceMembers)
        {
            var exact = targetMembers.Where(x => string.Equals(x, member.Name, StringComparison.Ordinal)).ToArray();
            var insensitive = targetMembers.Where(x => NamesEqual(x, member.Name)).ToArray();
            if (exact.Length == 1)
                builder.Add((member.Name, exact[0]));
            else if (insensitive.Length == 1)
                builder.Add((member.Name, insensitive[0]));
            else
                missing.Add(member.Name);
        }
        if (missing.Count > 0)
        {
            reason =
                $"source members {Quote(missing)} have no same-named target member; align the members or declare a [DomainFactory] for the pair";
            return false;
        }

        pairs = builder.ToImmutable();
        return true;
    }

    private static IReadOnlyList<IFieldSymbol> EnumMembers(ITypeSymbol type) =>
        type.GetMembers().OfType<IFieldSymbol>().Where(x => x.IsStatic && x.HasConstantValue).ToArray();

    private static bool HasFlagsAttribute(ITypeSymbol type) =>
        type.GetAttributes()
            .Any(x => string.Equals(x.AttributeClass?.ToDisplayString(), "System.FlagsAttribute", StringComparison.Ordinal));

    private static string Quote(IEnumerable<string> names) => string.Join(", ", names.Select(x => $"'{x}'"));
}
