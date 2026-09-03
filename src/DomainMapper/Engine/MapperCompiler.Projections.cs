using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    [SuppressMessage(
        "Maintainability",
        "MA0051",
        Justification = "Keeps projection declaration validation and cached member emission together."
    )]
    private void BuildProjections()
    {
        foreach (var projection in _projectionMethods)
        {
            if (!TryGetProjectionTypes(projection, out var sourceType, out var targetType))
            {
                ReportInvalidProjection(
                    projection,
                    "<root>",
                    "the declared method shape",
                    "Declare a parameterless method returning Expression<Func<TSource, TTarget>>."
                );
                continue;
            }
            if (!TryReadString(Attribute(projection, MapProjectionAttribute)!, 0, out var mappingName))
            {
                ReportInvalidProjection(projection, "<root>", "an invalid mapping reference", "Reference one mapping method by name.");
                continue;
            }

            var mappings = _mappingMethods
                .Where(x =>
                    NamesEqual(x.Name, mappingName)
                    && !x.ReturnsVoid
                    && x.Parameters.Length > 0
                    && TypesEqual(x.Parameters[0].Type, sourceType)
                    && TypesEqual(x.ReturnType, targetType)
                )
                .ToArray();
            if (mappings.Length != 1)
            {
                ReportInvalidProjection(
                    projection,
                    "<root>",
                    "a missing or ambiguous mapping contract",
                    "Reference one successfully generated create mapping with matching source and target types."
                );
                continue;
            }
            var mapping = mappings[0];
            if (!_successfulMappingMethods.Contains(mapping) || !_configurations.TryGetValue(mapping, out var configuration))
            {
                ReportInvalidProjection(
                    projection,
                    "<root>",
                    "an invalid mapping contract",
                    "Fix the referenced in-memory mapping before declaring its projection."
                );
                continue;
            }
            if (!ValidateProjectionEligibility(projection, mapping, configuration))
                continue;

            var expression = BuildProjectionExpression(
                sourceType,
                targetType,
                "source",
                new MappingContext(mapping.TypeParameters, ImmutableArray<MappingValue>.Empty, configuration),
                new HashSet<string>(StringComparer.Ordinal),
                out var failureMember
            );
            if (expression == null)
            {
                ReportInvalidProjection(
                    projection,
                    failureMember ?? "<root>",
                    "an unsupported construction or conversion",
                    "Use constructor/member initialization and the documented pure conversion subset, or keep this as an in-memory mapping."
                );
                continue;
            }

            var holderName = ReserveMemberName(
                $"__domainMapperProjection_{Sanitize(projection.Name)}_{StableHash(projection.ToDisplayString()):X8}Holder"
            );
            _supportMembers.Add(
                $"private static class {holderName}\n{{\n"
                    + "#if NET5_0_OR_GREATER\n"
                    + "    [global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(\"Expression-tree construction requires member metadata.\")]\n"
                    + "#endif\n"
                    + $"    static {holderName}() {{ }}\n"
                    + $"    internal static readonly {TypeName(projection.ReturnType)} Value = source => {expression};\n"
                    + "}"
            );
            _rootContracts.Add(
                new MappingContract(
                    projection.Name,
                    "#if NET5_0_OR_GREATER\n"
                        + "[global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(\"Expression-tree construction requires member metadata and is not supported by DomainMapper under trimming or native AOT.\")]\n"
                        + "#endif\n"
                        + BuildDeclaration(projection),
                    $"return {holderName}.Value;",
                    MappingShape.Create
                )
            );
        }
    }

    private bool ValidateProjectionEligibility(IMethodSymbol projection, IMethodSymbol mapping, MappingMethodConfiguration configuration)
    {
        var failures = new List<(string Member, string Operation, string Action)>();
        if (mapping.Parameters.Length != 1)
            failures.Add(("<root>", "additional mapping parameters", "Use an in-memory mapping for caller-supplied values."));
        if (ReadFactoryName(mapping) != null)
            failures.Add(("<root>", "a target factory", "Use a projection-safe constructor or member initializer."));
        if (configuration.CompletionHooks.Length > 0)
            failures.Add(("<root>", "completion hooks", "Keep completion hooks on the in-memory mapping only."));
        if (configuration.PreserveReferences)
            failures.Add(("<root>", "reference tracking", "Use reference tracking only for in-memory mappings."));
        if (configuration.MaximumDepth != null)
            failures.Add(("<root>", "bounded recursion", "Project an acyclic shape or use the in-memory mapping."));
        failures.AddRange(
            configuration.Conditions.Keys.Select(x =>
                (x, "conditional assignment", "Express the condition in the consumer query or use the in-memory mapping.")
            )
        );
        failures.AddRange(
            configuration.ComputedMembers.Keys.Select(x => (x, "a mapper method call", "Bind a source path or use the in-memory mapping."))
        );
        failures.AddRange(
            configuration.CollectionPolicies.Keys.Select(x =>
                (x, "existing-target collection mutation", "Collection mutation is not a projection operation.")
            )
        );
        foreach (var failure in failures)
            ReportInvalidProjection(projection, failure.Member, failure.Operation, failure.Action);
        return failures.Count == 0;
    }

    [SuppressMessage(
        "Maintainability",
        "MA0051",
        Justification = "Keeps projection construction fail-closed in one recursive planning flow."
    )]
    private string? BuildProjectionExpression(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        MappingContext context,
        ISet<string> visiting,
        out string? failureMember
    )
    {
        failureMember = null;
        if (TypesEqual(sourceType, targetType))
            return sourceExpression;

        if (targetType.IsReferenceType && targetType.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var target = targetType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            if (sourceType.IsReferenceType && sourceType.NullableAnnotation == NullableAnnotation.Annotated)
            {
                var source = sourceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                var mapped = BuildProjectionExpression(source, target, sourceExpression + "!", context, visiting, out failureMember);
                return mapped == null ? null : $"{sourceExpression} == null ? null : {mapped}";
            }
            return BuildProjectionExpression(sourceType, target, sourceExpression, context, visiting, out failureMember);
        }
        var conversion = _compilation.ClassifyConversion(sourceType, targetType);
        if (conversion.Exists && conversion.IsImplicit && !conversion.IsUserDefined)
            return sourceExpression;
        if (IsNullable(sourceType))
            return null;
        if (TryGetSequenceElement(sourceType, out _) || TryGetDictionaryTypes(sourceType, out _, out _))
            return null;

        var key = $"{TypeName(sourceType)}->{TypeName(targetType)}";
        if (!visiting.Add(key))
            return null;
        try
        {
            if (
                targetType
                    is not INamedTypeSymbol { SpecialType: SpecialType.None, TypeKind: TypeKind.Class or TypeKind.Struct } namedTarget
                || namedTarget.IsAbstract
            )
                return null;
            var configuration = RootConfiguration(context, sourceType, targetType);
            foreach (
                var constructor in namedTarget
                    .InstanceConstructors.Where(IsAccessible)
                    .Where(x => !IsRecordCopyConstructor(x, namedTarget))
                    .OrderByDescending(x => x.Parameters.Length)
            )
            {
                var arguments = new List<string>();
                var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var valid = true;
                foreach (var parameter in constructor.Parameters)
                {
                    var argument = BuildProjectionMemberValue(
                        sourceType,
                        targetType,
                        sourceExpression,
                        parameter.Name,
                        parameter.Type,
                        context,
                        visiting,
                        out failureMember
                    );
                    if (argument == null)
                    {
                        valid = false;
                        break;
                    }
                    arguments.Add(argument);
                    consumed.Add(parameter.Name);
                }
                if (!valid)
                    continue;

                var initializers = new List<string>();
                foreach (var member in SettableTargetMembers(targetType, configuration))
                {
                    if (consumed.Contains(member.Name) || configuration?.IgnoredTargets.Contains(member.Name) == true)
                        continue;
                    var value = BuildProjectionMemberValue(
                        sourceType,
                        targetType,
                        sourceExpression,
                        member.Name,
                        member.Type,
                        context,
                        visiting,
                        out failureMember
                    );
                    if (value == null)
                    {
                        if (configuration?.EnforceTarget == false && !member.IsRequired)
                            continue;
                        valid = false;
                        break;
                    }
                    initializers.Add($"{Escape(member.Name)} = {value}");
                }
                if (!valid)
                    continue;
                var initializer = initializers.Count == 0 ? string.Empty : $" {{ {string.Join(", ", initializers)} }}";
                return $"new {TypeName(targetType)}({string.Join(", ", arguments)}){initializer}";
            }
            return null;
        }
        finally
        {
            visiting.Remove(key);
        }
    }

    private string? BuildProjectionMemberValue(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        string targetMemberName,
        ITypeSymbol targetMemberType,
        MappingContext context,
        ISet<string> visiting,
        out string? failureMember
    )
    {
        failureMember = targetMemberName;
        var configuration = RootConfiguration(context, sourceType, targetType);
        string sourceValue;
        ITypeSymbol sourceValueType;
        if (configuration?.Bindings.TryGetValue(targetMemberName, out var binding) == true)
        {
            sourceValueType = EffectivePathType(binding.SourceMembers);
            sourceValue = BuildProjectionSourcePath(sourceExpression, binding.SourceMembers, sourceValueType);
        }
        else if (TryFindMember(ReadableMembers(sourceType), targetMemberName, out var sourceMember))
        {
            sourceValue = $"{sourceExpression}.{Escape(sourceMember.Name)}";
            sourceValueType = sourceMember.Type;
        }
        else
        {
            return null;
        }

        if (configuration?.NullSubstitutes.TryGetValue(targetMemberName, out var substitute) == true && IsNullable(sourceValueType))
        {
            var mapped = BuildProjectionExpression(
                NonNullableType(sourceValueType),
                targetMemberType,
                NonNullExpression(sourceValue, sourceValueType),
                context,
                visiting,
                out failureMember
            );
            return mapped == null ? null : $"{sourceValue} == null ? {substitute} : {mapped}";
        }
        if (configuration?.NullBehaviors.TryGetValue(targetMemberName, out var behavior) == true && behavior != 0)
            return null;
        return BuildProjectionExpression(sourceValueType, targetMemberType, sourceValue, context, visiting, out failureMember);
    }

    private static string BuildProjectionSourcePath(string sourceExpression, ImmutableArray<MappingMember> path, ITypeSymbol effectiveType)
    {
        var expression = sourceExpression;
        var nullablePrefixes = new List<string>();
        ITypeSymbol? currentType = null;
        foreach (var member in path)
        {
            if (currentType != null && IsNullable(currentType))
            {
                nullablePrefixes.Add(expression);
                expression = NonNullExpression(expression, currentType);
            }
            expression += "." + Escape(member.Name);
            currentType = member.Type;
        }
        foreach (var prefix in nullablePrefixes.AsEnumerable().Reverse())
            expression = $"{prefix} == null ? default({TypeName(effectiveType)}) : {expression}";
        return expression;
    }

    private static bool TryGetProjectionTypes(IMethodSymbol method, out ITypeSymbol sourceType, out ITypeSymbol targetType)
    {
        sourceType = null!;
        targetType = null!;
        if (
            !method.IsStatic
            || method.Parameters.Length != 0
            || method.TypeParameters.Length != 0
            || method.ReturnType is not INamedTypeSymbol expression
        )
            return false;
        if (
            !string.Equals(expression.OriginalDefinition.MetadataName, "Expression`1", StringComparison.Ordinal)
            || !string.Equals(
                expression.OriginalDefinition.ContainingNamespace.ToDisplayString(),
                "System.Linq.Expressions",
                StringComparison.Ordinal
            )
        )
            return false;
        if (
            expression.TypeArguments[0] is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } delegateType
            || !string.Equals(delegateType.OriginalDefinition.MetadataName, "Func`2", StringComparison.Ordinal)
            || !string.Equals(delegateType.OriginalDefinition.ContainingNamespace.ToDisplayString(), "System", StringComparison.Ordinal)
            || invoke.Parameters.Length != 1
        )
            return false;
        sourceType = invoke.Parameters[0].Type;
        targetType = invoke.ReturnType;
        return true;
    }

    private void ReportInvalidProjection(IMethodSymbol method, string member, string operation, string action) =>
        _diagnostics.Add(
            DiagnosticData.Create(
                MapperDiagnostics.InvalidProjection,
                method.Locations.FirstOrDefault(),
                method.Name,
                member,
                operation,
                action
            )
        );
}
