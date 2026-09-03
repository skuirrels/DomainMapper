using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    [SuppressMessage("Maintainability", "MA0051", Justification = "Keeps the complete per-method configuration validation flow auditable.")]
    private MappingMethodConfiguration? BuildConfiguration(
        IMethodSymbol method,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        bool isUpdate
    )
    {
        if (!HasExplicitConfiguration(method))
            return BuildConventionConfiguration(method, sourceType, targetType);

        var valid = true;
        var comparer = StringComparer.OrdinalIgnoreCase;
        var bindings = ImmutableDictionary.CreateBuilder<string, MemberBinding>(comparer);
        var ignoredTargets = ImmutableHashSet.CreateBuilder<string>(comparer);
        var ignoredSources = ImmutableHashSet.CreateBuilder<string>(comparer);
        var nullBehaviors = ImmutableDictionary.CreateBuilder<string, int>(comparer);
        var nullSubstitutes = ImmutableDictionary.CreateBuilder<string, string>(comparer);
        var collectionPolicies = ImmutableDictionary.CreateBuilder<string, int>(comparer);
        var computedMembers = ImmutableDictionary.CreateBuilder<string, IMethodSymbol>(comparer);
        var conditions = ImmutableDictionary.CreateBuilder<string, IMethodSymbol>(comparer);
        var completionHooks = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var completionHookMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var sourceMembers = AllReadableMembers(sourceType);
        var targetMembers = GetAllMappingMembers(targetType).ToArray();
        var preserveReferences = HasAttribute(method, MapReferenceTrackingAttribute);

        if (preserveReferences && (isUpdate || !sourceType.IsReferenceType || !targetType.IsReferenceType))
        {
            _diagnostics.Add(
                DiagnosticData.Create(
                    MapperDiagnostics.UnsupportedReferenceTracking,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    "tracking requires a create mapping between reference types"
                )
            );
            valid = false;
        }

        var completeness = 0;
        var completenessAttribute = Attribute(method, MappingCompletenessAttribute);
        if (completenessAttribute?.ConstructorArguments is [{ Value: int configuredCompleteness }])
            completeness = configuredCompleteness;
        if (completeness is < 0 or > 3)
        {
            ReportInvalidConfiguration(method, $"completeness policy value '{completeness}' is not defined");
            valid = false;
        }
        if (completeness == 3)
            _diagnostics.Add(DiagnosticData.Create(MapperDiagnostics.CompletenessDisabled, method.Locations.FirstOrDefault(), method.Name));

        int? maximumDepth = null;
        var depthExhaustionBehavior = 0;
        var maxDepthAttribute = Attribute(method, MapMaxDepthAttribute);
        if (maxDepthAttribute != null)
        {
            if (maxDepthAttribute.ConstructorArguments is not [{ Value: int configuredDepth }] || configuredDepth <= 0)
            {
                ReportInvalidConfiguration(method, "maximum mapping depth must be greater than zero");
                valid = false;
            }
            else
            {
                maximumDepth = configuredDepth;
                var configuredBehavior = maxDepthAttribute
                    .NamedArguments.FirstOrDefault(x => string.Equals(x.Key, "ExhaustionBehavior", StringComparison.Ordinal))
                    .Value.Value;
                if (configuredBehavior is int behavior)
                    depthExhaustionBehavior = behavior;
                if (depthExhaustionBehavior is < 0 or > 1)
                {
                    ReportInvalidConfiguration(method, $"depth exhaustion behavior value '{depthExhaustionBehavior}' is not defined");
                    valid = false;
                }
            }
        }

        foreach (var attribute in Attributes(method, MapCollectionAttribute))
        {
            if (
                !isUpdate
                || !TryReadString(attribute, 0, out var targetName)
                || attribute.ConstructorArguments.Length != 2
                || attribute.ConstructorArguments[1].Value is not int policy
                || policy is < 0 or > 2
                || !TryFindMember(targetMembers, targetName, out var targetMember)
                || !targetMember.CanRead
            )
            {
                ReportInvalidConfiguration(
                    method,
                    "a collection policy requires an existing-target mapping and a readable target collection member"
                );
                valid = false;
                continue;
            }

            if (!TryGetSequenceElement(targetMember.Type, out _) && !TryGetDictionaryTypes(targetMember.Type, out _, out _))
            {
                ReportInvalidConfiguration(method, $"collection policy target '{targetName}' is not a supported collection");
                valid = false;
                continue;
            }
            if (policy == 0 && (!targetMember.CanWrite || targetMember.IsInitOnly))
            {
                ReportInvalidConfiguration(method, $"Replace collection policy for '{targetName}' requires a writable target member");
                valid = false;
                continue;
            }
            if (policy is 1 or 2 && !CanMutateCollection(targetMember.Type))
            {
                ReportInvalidConfiguration(
                    method,
                    $"collection policy for '{targetName}' requires a mutable ICollection<T> or IDictionary<TKey, TValue> target"
                );
                valid = false;
                continue;
            }
            if (!collectionPolicies.TryAdd(targetName, policy))
            {
                ReportInvalidConfiguration(method, $"target member '{targetName}' has more than one collection policy");
                valid = false;
            }
        }

        if (!LoadIncludedBindings(method, sourceType, targetType, bindings, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)))
            valid = false;

        var localBindingTargets = new HashSet<string>(comparer);
        foreach (var attribute in Attributes(method, MapMemberAttribute))
        {
            if (!TryReadTwoStrings(attribute, out var targetName, out var sourcePath))
            {
                ReportInvalidConfiguration(method, "member binding arguments must be non-empty compile-time strings");
                valid = false;
                continue;
            }

            if (!TryFindMember(targetMembers, targetName, out var targetMember) || !IsEligibleTargetMember(targetMember))
            {
                ReportInvalidConfiguration(
                    method,
                    $"target member '{targetName}' does not exist or is ambiguous on '{targetType.ToDisplayString()}'"
                );
                valid = false;
                continue;
            }

            if (!TryResolveSourcePath(sourceType, sourcePath, out var path))
            {
                ReportInvalidConfiguration(
                    method,
                    $"source path '{sourcePath}' is missing, ambiguous, or inaccessible on '{sourceType.ToDisplayString()}'"
                );
                valid = false;
                continue;
            }

            if (!localBindingTargets.Add(targetName))
            {
                ReportInvalidConfiguration(method, $"target member '{targetName}' has more than one explicit binding");
                valid = false;
                continue;
            }

            bindings[targetName] = new MemberBinding(targetName, sourcePath, path);
        }

        foreach (var attribute in Attributes(method, IgnoreTargetMemberAttribute))
        {
            if (
                !TryReadString(attribute, 0, out var memberName)
                || !TryFindMember(targetMembers, memberName, out var member)
                || !IsEligibleTargetMember(member)
            )
            {
                ReportInvalidConfiguration(method, "an ignored target member is missing, ambiguous, or invalid");
                valid = false;
                continue;
            }
            if (!ignoredTargets.Add(memberName))
            {
                ReportInvalidConfiguration(method, $"target member '{memberName}' is ignored more than once");
                valid = false;
            }
        }

        foreach (var attribute in Attributes(method, IgnoreSourceMemberAttribute))
        {
            if (!TryReadString(attribute, 0, out var memberName) || !TryFindMember(sourceMembers, memberName, out _))
            {
                ReportInvalidConfiguration(method, "an ignored source member is missing, ambiguous, or inaccessible");
                valid = false;
                continue;
            }
            if (!ignoredSources.Add(memberName))
            {
                ReportInvalidConfiguration(method, $"source member '{memberName}' is ignored more than once");
                valid = false;
            }
        }

        ImmutableHashSet<string>? onlyTargets = null;
        var onlyAttribute = Attribute(method, MapOnlyTargetMembersAttribute);
        if (onlyAttribute != null)
        {
            if (!isUpdate)
            {
                ReportInvalidConfiguration(method, "MapOnlyTargetMembers is valid only for existing-target mappings");
                valid = false;
            }

            var onlyBuilder = ImmutableHashSet.CreateBuilder<string>(comparer);
            if (
                onlyAttribute.ConstructorArguments is not [{ Kind: TypedConstantKind.Array } onlyValues]
                || onlyValues.Values.Any(x => x.Value is not string text || text.Length == 0)
            )
            {
                ReportInvalidConfiguration(method, "an existing-target allow-list contains an invalid member name");
                valid = false;
            }
            foreach (var memberName in ReadStringArray(onlyAttribute))
            {
                if (
                    !TryFindMember(targetMembers, memberName, out var member)
                    || (
                        (!member.CanWrite || member.IsInitOnly)
                        && (!collectionPolicies.TryGetValue(memberName, out var policy) || policy is not (1 or 2))
                    )
                )
                {
                    ReportInvalidConfiguration(method, $"allow-listed target member '{memberName}' is missing, ambiguous, or not writable");
                    valid = false;
                    continue;
                }
                if (!onlyBuilder.Add(memberName))
                {
                    ReportInvalidConfiguration(method, $"target member '{memberName}' appears more than once in the update allow-list");
                    valid = false;
                }
            }
            if (onlyBuilder.Count == 0)
            {
                ReportInvalidConfiguration(method, "an existing-target allow-list cannot be empty");
                valid = false;
            }
            onlyTargets = onlyBuilder.ToImmutable();
        }

        foreach (var attribute in Attributes(method, MapNullAttribute))
        {
            if (
                !TryReadString(attribute, 0, out var targetName)
                || attribute.ConstructorArguments.Length != 2
                || attribute.ConstructorArguments[1].Value is not int behavior
                || behavior is < 0 or > 3
                || !TryFindMember(targetMembers, targetName, out var targetMember)
                || !IsEligibleTargetMember(targetMember)
            )
            {
                ReportInvalidConfiguration(method, "a null-policy target member is missing, ambiguous, or invalid");
                valid = false;
                continue;
            }
            if (behavior == 1 && !isUpdate)
            {
                ReportInvalidConfiguration(method, $"PreserveTarget null behavior for '{targetName}' requires an existing-target mapping");
                valid = false;
                continue;
            }
            if (behavior == 0 && !IsNullable(targetMember.Type))
            {
                ReportInvalidConfiguration(method, $"Assign null behavior for '{targetName}' requires a nullable target member");
                valid = false;
                continue;
            }
            if (behavior == 3 && BuildEmptyCollectionExpression(targetMember.Type) == null)
            {
                ReportInvalidConfiguration(
                    method,
                    $"EmptyCollection null behavior for '{targetName}' requires a supported collection target"
                );
                valid = false;
                continue;
            }
            if (!nullBehaviors.TryAdd(targetName, behavior))
            {
                ReportInvalidConfiguration(method, $"target member '{targetName}' has more than one null policy");
                valid = false;
            }
        }

        foreach (var attribute in Attributes(method, MapNullSubstituteAttribute))
        {
            if (
                !TryReadString(attribute, 0, out var targetName)
                || attribute.ConstructorArguments.Length != 2
                || !TryFindMember(targetMembers, targetName, out var targetMember)
                || !IsEligibleTargetMember(targetMember)
                || BuildConstantExpression(attribute.ConstructorArguments[1], targetMember.Type) is not { } substitute
            )
            {
                ReportInvalidConfiguration(method, "a null substitute is invalid or incompatible with its target member");
                valid = false;
                continue;
            }
            if (nullBehaviors.ContainsKey(targetName) || !nullSubstitutes.TryAdd(targetName, substitute))
            {
                ReportInvalidConfiguration(method, $"target member '{targetName}' has more than one null policy");
                valid = false;
            }
        }

        foreach (var helper in ConfigurationHelpers(method.Name))
        {
            foreach (var attribute in Attributes(helper, MapTargetMemberAttribute))
            {
                if (!TryReadTwoStrings(attribute, out var mappingName, out var targetName) || !NamesEqual(mappingName, method.Name))
                    continue;
                if (
                    !helper.IsStatic
                    || helper.ReturnsVoid
                    || helper.TypeParameters.Length > 0
                    || !TryFindMember(targetMembers, targetName, out var targetMember)
                    || !IsEligibleTargetMember(targetMember)
                )
                {
                    ReportInvalidConfiguration(
                        method,
                        $"computed-member method '{helper.Name}' is not a supported static method or targets an invalid member"
                    );
                    valid = false;
                    continue;
                }
                if (
                    !CanConvert(
                        helper.ReturnType,
                        targetMember.Type,
                        new MappingContext(method.TypeParameters, ImmutableArray<MappingValue>.Empty),
                        new HashSet<string>(StringComparer.Ordinal)
                    )
                )
                {
                    ReportInvalidConfiguration(
                        method,
                        $"computed-member method '{helper.Name}' returns '{helper.ReturnType.ToDisplayString()}', which cannot map to '{targetMember.Type.ToDisplayString()}'"
                    );
                    valid = false;
                    continue;
                }
                if (!computedMembers.TryAdd(targetName, helper))
                {
                    ReportInvalidConfiguration(method, $"target member '{targetName}' has more than one computed-member method");
                    valid = false;
                }
            }

            foreach (var attribute in Attributes(helper, MapConditionAttribute))
            {
                if (!TryReadTwoStrings(attribute, out var mappingName, out var targetName) || !NamesEqual(mappingName, method.Name))
                    continue;
                if (
                    !helper.IsStatic
                    || helper.TypeParameters.Length > 0
                    || helper.ReturnType.SpecialType != SpecialType.System_Boolean
                    || !TryFindMember(targetMembers, targetName, out var targetMember)
                    || !IsEligibleTargetMember(targetMember)
                )
                {
                    ReportInvalidConfiguration(
                        method,
                        $"condition method '{helper.Name}' must be a non-generic static Boolean method targeting a valid member"
                    );
                    valid = false;
                    continue;
                }
                if (!conditions.TryAdd(targetName, helper))
                {
                    ReportInvalidConfiguration(method, $"target member '{targetName}' has more than one condition");
                    valid = false;
                }
            }

            foreach (var attribute in Attributes(helper, MapAfterAttribute))
            {
                if (!TryReadString(attribute, 0, out var mappingName) || !NamesEqual(mappingName, method.Name))
                    continue;
                if (!helper.IsStatic || !helper.ReturnsVoid || helper.TypeParameters.Length > 0)
                {
                    ReportInvalidConfiguration(method, $"completion hook '{helper.Name}' must be a non-generic static void method");
                    valid = false;
                    continue;
                }
                if (!completionHookMethods.Add(helper))
                {
                    ReportInvalidConfiguration(method, $"completion hook '{helper.Name}' is configured more than once");
                    valid = false;
                    continue;
                }
                completionHooks.Add(helper);
            }
        }

        foreach (var targetName in bindings.Keys.Concat(computedMembers.Keys))
        {
            if (ignoredTargets.Contains(targetName))
            {
                ReportInvalidConfiguration(method, $"target member '{targetName}' is both configured and ignored");
                valid = false;
            }
        }

        foreach (var targetName in collectionPolicies.Keys)
        {
            if (ignoredTargets.Contains(targetName))
            {
                ReportInvalidConfiguration(method, $"collection policy for target member '{targetName}' cannot be combined with an ignore");
                valid = false;
            }
            if (computedMembers.ContainsKey(targetName) || nullSubstitutes.ContainsKey(targetName))
            {
                ReportInvalidConfiguration(
                    method,
                    $"collection policy for target member '{targetName}' cannot be combined with a computed member or null substitute"
                );
                valid = false;
            }
        }

        foreach (var targetName in nullBehaviors.Keys.Concat(nullSubstitutes.Keys))
        {
            if (ignoredTargets.Contains(targetName) || computedMembers.ContainsKey(targetName))
            {
                ReportInvalidConfiguration(
                    method,
                    $"null policy for target member '{targetName}' cannot be combined with an ignore or computed-member method"
                );
                valid = false;
            }
        }

        foreach (var targetName in conditions.Keys)
        {
            if (ignoredTargets.Contains(targetName))
            {
                ReportInvalidConfiguration(method, $"condition for target member '{targetName}' cannot be combined with an ignore");
                valid = false;
            }
        }

        if (ConfigurationHelpers(method.Name).Any() && _mappingMethods.Count(x => NamesEqual(x.Name, method.Name)) > 1)
        {
            ReportInvalidConfiguration(method, $"helper configuration cannot target overloaded mapping name '{method.Name}'");
            valid = false;
        }

        foreach (var targetName in nullBehaviors.Keys.Concat(nullSubstitutes.Keys).Distinct(comparer))
        {
            if (
                !TryGetConfiguredSourceType(sourceType, targetName, bindings, out var configuredSourceType)
                || !IsNullable(configuredSourceType)
            )
            {
                ReportInvalidConfiguration(
                    method,
                    $"null policy for target member '{targetName}' does not resolve to a nullable source value"
                );
                valid = false;
            }
        }

        return valid
            ? new MappingMethodConfiguration(
                method,
                sourceType,
                targetType,
                completeness,
                bindings.ToImmutable(),
                ignoredTargets.ToImmutable(),
                ignoredSources.ToImmutable(),
                onlyTargets,
                nullBehaviors.ToImmutable(),
                nullSubstitutes.ToImmutable(),
                computedMembers.ToImmutable(),
                conditions.ToImmutable(),
                completionHooks.ToImmutable(),
                maximumDepth,
                depthExhaustionBehavior,
                collectionPolicies.ToImmutable(),
                preserveReferences
            )
            : null;
    }

    private bool HasExplicitConfiguration(IMethodSymbol method)
    {
        if (_configurationHelpers.ContainsKey(method.Name))
            return true;

        foreach (var attribute in method.GetAttributes())
        {
            foreach (var attributeName in ExplicitConfigurationAttributes)
            {
                if (IsAttribute(attribute, attributeName))
                    return true;
            }
        }

        return false;
    }

    private static MappingMethodConfiguration BuildConventionConfiguration(
        IMethodSymbol method,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    ) =>
        new(
            method,
            sourceType,
            targetType,
            0,
            ImmutableDictionary<string, MemberBinding>.Empty,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty,
            null,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, IMethodSymbol>.Empty,
            ImmutableDictionary<string, IMethodSymbol>.Empty,
            ImmutableArray<IMethodSymbol>.Empty,
            null,
            0,
            ImmutableDictionary<string, int>.Empty,
            false
        );

    private bool LoadIncludedBindings(
        IMethodSymbol method,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ImmutableDictionary<string, MemberBinding>.Builder bindings,
        ISet<IMethodSymbol> visiting
    )
    {
        if (!visiting.Add(method))
        {
            ReportInvalidConfiguration(method, "included mappings contain a cycle");
            return false;
        }

        var valid = true;
        try
        {
            foreach (var include in Attributes(method, IncludeMappingAttribute))
            {
                if (!TryResolveIncludedBindings(method, method, include, sourceType, targetType, visiting, out var includedBindings))
                    valid = false;

                foreach (var binding in includedBindings)
                {
                    if (!bindings.TryAdd(binding.Key, binding.Value))
                    {
                        ReportInvalidConfiguration(method, $"included mappings conflict for target member '{binding.Key}'");
                        valid = false;
                    }
                }
            }
        }
        finally
        {
            visiting.Remove(method);
        }

        return valid;
    }

    [SuppressMessage("Maintainability", "MA0051", Justification = "Keeps recursive include validation in one auditable flow.")]
    private bool TryResolveIncludedBindings(
        IMethodSymbol reportingMethod,
        IMethodSymbol includingMethod,
        AttributeData include,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ISet<IMethodSymbol> visiting,
        out ImmutableDictionary<string, MemberBinding> resolvedBindings
    )
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var bindings = ImmutableDictionary.CreateBuilder<string, MemberBinding>(comparer);
        resolvedBindings = bindings.ToImmutable();

        if (!TryReadString(include, 0, out var mappingName))
        {
            ReportInvalidConfiguration(reportingMethod, "an included mapping name must be a non-empty compile-time string");
            return false;
        }

        var candidates = _mappingMethods
            .Where(x => NamesEqual(x.Name, mappingName) && !SymbolEqualityComparer.Default.Equals(x, includingMethod))
            .ToArray();
        if (candidates.Length != 1)
        {
            ReportInvalidConfiguration(reportingMethod, $"included mapping '{mappingName}' is missing or ambiguous");
            return false;
        }

        var included = candidates[0];
        if (!visiting.Add(included))
        {
            ReportInvalidConfiguration(reportingMethod, "included mappings contain a cycle");
            return false;
        }

        var valid = true;
        try
        {
            foreach (var nestedInclude in Attributes(included, IncludeMappingAttribute))
            {
                if (
                    !TryResolveIncludedBindings(
                        reportingMethod,
                        included,
                        nestedInclude,
                        sourceType,
                        targetType,
                        visiting,
                        out var nestedBindings
                    )
                )
                    valid = false;

                foreach (var binding in nestedBindings)
                {
                    if (!bindings.TryAdd(binding.Key, binding.Value))
                    {
                        ReportInvalidConfiguration(reportingMethod, $"included mappings conflict for target member '{binding.Key}'");
                        valid = false;
                    }
                }
            }

            var localBindingTargets = new HashSet<string>(comparer);
            foreach (var attribute in Attributes(included, MapMemberAttribute))
            {
                if (
                    !TryReadTwoStrings(attribute, out var targetName, out var sourcePath)
                    || !TryFindMember(GetAllMappingMembers(targetType).ToArray(), targetName, out var targetMember)
                    || !IsEligibleTargetMember(targetMember)
                    || !TryResolveSourcePath(sourceType, sourcePath, out var path)
                )
                {
                    ReportInvalidConfiguration(
                        reportingMethod,
                        $"included binding from '{mappingName}' is not valid for '{sourceType.ToDisplayString()}' to '{targetType.ToDisplayString()}'"
                    );
                    valid = false;
                    continue;
                }

                if (!localBindingTargets.Add(targetName))
                {
                    ReportInvalidConfiguration(
                        reportingMethod,
                        $"included mapping '{mappingName}' configures target member '{targetName}' more than once"
                    );
                    valid = false;
                    continue;
                }

                bindings[targetName] = new MemberBinding(targetName, sourcePath, path);
            }
        }
        finally
        {
            visiting.Remove(included);
        }

        resolvedBindings = bindings.ToImmutable();
        return valid;
    }

    private static bool HasConfiguredOrConventionValue(
        MappingMethodConfiguration? configuration,
        IReadOnlyList<MappingMember> sourceMembers,
        string targetMemberName
    ) =>
        configuration?.Bindings.ContainsKey(targetMemberName) == true
        || configuration?.ComputedMembers.ContainsKey(targetMemberName) == true
        || TryFindMember(sourceMembers, targetMemberName, out _);

    private static bool IsEligibleTargetMember(MappingMember member) => member.CanRead || member.CanWrite;

    private bool TryGetConfiguredSourceType(
        ITypeSymbol sourceType,
        string targetMemberName,
        IReadOnlyDictionary<string, MemberBinding> bindings,
        out ITypeSymbol configuredSourceType
    )
    {
        if (bindings.TryGetValue(targetMemberName, out var binding))
        {
            configuredSourceType = EffectivePathType(binding.SourceMembers);
            return true;
        }
        if (TryFindMember(ReadableMembers(sourceType), targetMemberName, out var sourceMember))
        {
            configuredSourceType = sourceMember.Type;
            return true;
        }
        configuredSourceType = null!;
        return false;
    }

    [SuppressMessage("Maintainability", "MA0051", Justification = "Keeps source-completeness accounting in one auditable flow.")]
    private bool ValidateSourceCompleteness(
        MappingMethodConfiguration configuration,
        IMethodSymbol? factory,
        ISet<string>? explicitFactoryParameters
    )
    {
        if (!configuration.EnforceSource)
            return true;

        var consumed = new HashSet<string>(configuration.IgnoredSources, StringComparer.OrdinalIgnoreCase);
        var conventionTargets = new Dictionary<string, MappingMember>(StringComparer.OrdinalIgnoreCase);
        if (factory == null)
        {
            foreach (var member in GetTargetMappingMembers(configuration.TargetType, configuration))
            {
                if (!IsEligibleTargetMember(member))
                    continue;
                if (configuration.IgnoredTargets.Contains(member.Name))
                    continue;
                if (configuration.OnlyTargets != null && !configuration.OnlyTargets.Contains(member.Name))
                    continue;
                conventionTargets.TryAdd(member.Name, member);
            }
        }
        else
        {
            foreach (var parameter in factory.Parameters)
            {
                if (explicitFactoryParameters?.Contains(parameter.Name) != true)
                    conventionTargets.TryAdd(parameter.Name, new MappingMember(parameter, parameter.Type, true, false, false, false));
            }
        }

        foreach (var binding in configuration.Bindings.Values.Where(x => conventionTargets.ContainsKey(x.TargetMember)))
        {
            consumed.Add(binding.SourceMembers[0].Name);
        }

        var context = BuildSourceCompletenessContext(configuration, factory);
        foreach (var sourceMember in ReadableMembers(configuration.SourceType))
        {
            if (
                conventionTargets.TryGetValue(sourceMember.Name, out var targetMember)
                && !configuration.ComputedMembers.ContainsKey(targetMember.Name)
                && CanConsumeConventionSource(configuration, sourceMember.Type, targetMember.Type, targetMember.Name, context)
            )
                consumed.Add(sourceMember.Name);
        }

        foreach (var computed in configuration.ComputedMembers)
        {
            if (!conventionTargets.ContainsKey(computed.Key))
                continue;
            if (computed.Value.Parameters.Any(x => SymbolEqualityComparer.Default.Equals(x.Type, configuration.SourceType)))
            {
                foreach (var sourceMember in AllReadableMembers(configuration.SourceType))
                {
                    consumed.Add(sourceMember.Name);
                }
                break;
            }

            if (configuration.Bindings.TryGetValue(computed.Key, out var binding))
                consumed.Add(binding.SourceMembers[0].Name);
            else if (TryFindMember(ReadableMembers(configuration.SourceType), computed.Key, out var sourceMember))
                consumed.Add(sourceMember.Name);
        }

        var valid = true;
        foreach (var member in AllReadableMembers(configuration.SourceType).Where(x => !consumed.Contains(x.Name)))
        {
            _diagnostics.Add(
                DiagnosticData.Create(
                    MapperDiagnostics.IncompleteSource,
                    configuration.Method.Locations.FirstOrDefault(),
                    configuration.Method.Name,
                    member.Name
                )
            );
            valid = false;
        }
        return valid;
    }

    private MappingContext BuildSourceCompletenessContext(MappingMethodConfiguration configuration, IMethodSymbol? factory)
    {
        var additionalValues = configuration.Method.ReturnsVoid
            ? ImmutableArray<MappingValue>.Empty
            : configuration.Method.Parameters.Skip(1).Select(x => new MappingValue(x.Name, x.Type, string.Empty)).ToImmutableArray();
        var ambientValues =
            factory == null
                ? additionalValues
                : additionalValues
                    .Concat(
                        ReadableMembers(configuration.SourceType)
                            .Where(x => !additionalValues.Any(y => NamesEqual(x.Name, y.Name)))
                            .Select(x => new MappingValue(x.Name, x.Type, string.Empty))
                    )
                    .ToImmutableArray();
        return new MappingContext(configuration.Method.TypeParameters, ambientValues, configuration);
    }

    private bool CanConsumeConventionSource(
        MappingMethodConfiguration configuration,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string targetMemberName,
        MappingContext context
    )
    {
        if (
            IsNullable(sourceType)
            && (
                configuration.NullSubstitutes.ContainsKey(targetMemberName)
                || configuration.NullBehaviors.TryGetValue(targetMemberName, out var behavior) && behavior is 1 or 2 or 3
            )
        )
            sourceType = NonNullableType(sourceType);

        return CanConvert(sourceType, targetType, context, new HashSet<string>(StringComparer.Ordinal));
    }

    private bool TryResolveSourcePath(ITypeSymbol sourceType, string sourcePath, out ImmutableArray<MappingMember> path)
    {
        var builder = ImmutableArray.CreateBuilder<MappingMember>();
        var currentType = sourceType;
        foreach (var segment in sourcePath.Split('.'))
        {
            if (segment.Length == 0 || !TryFindMember(AllReadableMembers(NonNullableType(currentType)), segment, out var member))
            {
                path = ImmutableArray<MappingMember>.Empty;
                return false;
            }
            builder.Add(member);
            currentType = member.Type;
        }

        path = builder.ToImmutable();
        return path.Length > 0;
    }

    private static string BuildSourcePathExpression(string sourceExpression, ImmutableArray<MappingMember> path)
    {
        var builder = new StringBuilder(sourceExpression);
        ITypeSymbol? currentType = null;
        for (var index = 0; index < path.Length; index++)
        {
            if (index == 0 || currentType == null || !IsNullable(currentType))
                builder.Append('.');
            else
                builder.Append("?.");
            builder.Append(Escape(path[index].Name));
            currentType = path[index].Type;
        }
        return builder.ToString();
    }

    private ITypeSymbol EffectivePathType(ImmutableArray<MappingMember> path)
    {
        var leafType = path[^1].Type;
        if (!path.Take(path.Length - 1).Any(x => IsNullable(x.Type)))
            return leafType;
        if (leafType.IsReferenceType)
            return leafType.WithNullableAnnotation(NullableAnnotation.Annotated);
        if (leafType is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return leafType;
        return _compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(leafType);
    }

    private string? BuildConstantExpression(TypedConstant constant, ITypeSymbol targetType)
    {
        if (constant.IsNull)
            return IsNullable(targetType) ? "null" : null;
        if (constant.Value == null || constant.Type == null)
            return null;

        var conversion = _compilation.ClassifyConversion(constant.Type, targetType);
        var nonNullableTarget = NonNullableType(targetType);
        var targetIsEnum = nonNullableTarget.TypeKind == TypeKind.Enum;
        if (!conversion.IsImplicit && !targetIsEnum)
            return null;

        var literal = targetIsEnum ? NumericLiteral(constant.Value) : ConstantLiteral(constant.Value);
        return literal == null ? null : $"({TypeName(targetType)})({literal})";
    }

    private static string? ConstantLiteral(object value) =>
        value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, true),
            char character => SymbolDisplay.FormatLiteral(character, true),
            bool boolean => boolean ? "true" : "false",
            float number => number.ToString("R", CultureInfo.InvariantCulture) + "F",
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
            _ => NumericLiteral(value),
        };

    private static string? NumericLiteral(object value) =>
        value switch
        {
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            byte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            _ => null,
        };

    private void ValidateConfigurationHelpers()
    {
        var mappingNames = _mappingMethods.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var helper in _mapperType.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (
                var attribute in Attributes(helper, MapTargetMemberAttribute)
                    .Concat(Attributes(helper, MapConditionAttribute))
                    .Concat(Attributes(helper, MapAfterAttribute))
            )
            {
                if (TryReadString(attribute, 0, out var mappingName) && !mappingNames.Contains(mappingName))
                    _diagnostics.Add(
                        DiagnosticData.Create(
                            MapperDiagnostics.InvalidConfiguration,
                            helper.Locations.FirstOrDefault(),
                            mappingName,
                            $"configuration method '{helper.Name}' refers to no partial mapping method"
                        )
                    );
            }
        }
    }

    private IEnumerable<IMethodSymbol> ConfigurationHelpers(string mappingMethodName) =>
        _configurationHelpers.TryGetValue(mappingMethodName, out var helpers) ? helpers : ImmutableArray<IMethodSymbol>.Empty;

    private IReadOnlyDictionary<string, ImmutableArray<IMethodSymbol>> IndexConfigurationHelpers(INamedTypeSymbol mapperType)
    {
        var helpers = new Dictionary<string, HashSet<IMethodSymbol>>(StringComparer.OrdinalIgnoreCase);
        foreach (var helper in mapperType.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (
                var attribute in Attributes(helper, MapTargetMemberAttribute)
                    .Concat(Attributes(helper, MapConditionAttribute))
                    .Concat(Attributes(helper, MapAfterAttribute))
            )
            {
                if (!TryReadString(attribute, 0, out var mappingName))
                    continue;
                if (!helpers.TryGetValue(mappingName, out var methods))
                {
                    methods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                    helpers.Add(mappingName, methods);
                }
                methods.Add(helper);
            }
        }

        return helpers.ToDictionary(
            x => x.Key,
            x => x.Value.OrderBy(y => y.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue).ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase
        );
    }

    private AttributeData? Attribute(ISymbol symbol, string attributeName) => Attributes(symbol, attributeName).FirstOrDefault();

    private IEnumerable<AttributeData> Attributes(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().Where(x => IsAttribute(x, attributeName));

    private bool IsAttribute(AttributeData attribute, string attributeName)
    {
        if (attribute.AttributeClass is not { } attributeClass)
            return false;
        if (!_attributeTypes.TryGetValue(attributeName, out var candidates))
        {
            candidates = _compilation.GetTypesByMetadataName(attributeName);
            _attributeTypes.Add(attributeName, candidates);
        }
        foreach (var candidate in candidates)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, attributeClass))
                return true;
        }
        return false;
    }

    private static bool TryReadTwoStrings(AttributeData attribute, out string first, out string second)
    {
        var firstValid = TryReadString(attribute, 0, out first);
        var secondValid = TryReadString(attribute, 1, out second);
        return firstValid && secondValid;
    }

    private static bool TryReadString(AttributeData attribute, int index, out string value)
    {
        if (attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is string text && text.Length > 0)
        {
            value = text;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static IEnumerable<string> ReadStringArray(AttributeData attribute)
    {
        if (attribute.ConstructorArguments is not [{ Kind: TypedConstantKind.Array } values])
            yield break;
        foreach (var value in values.Values)
        {
            if (value.Value is string text && text.Length > 0)
                yield return text;
        }
    }

    private void ReportInvalidConfiguration(IMethodSymbol method, string reason) =>
        _diagnostics.Add(
            DiagnosticData.Create(MapperDiagnostics.InvalidConfiguration, method.Locations.FirstOrDefault(), method.Name, reason)
        );

    private string? ReadFactoryName(IMethodSymbol method) =>
        Attribute(method, MapToFactoryAttribute)?.ConstructorArguments is [{ Value: string value }] ? value : null;

    private int ReadDomainFactoryInput(IMethodSymbol method)
    {
        var attribute = Attribute(method, DomainFactoryAttribute)!;
        var input = attribute.NamedArguments.FirstOrDefault(x => string.Equals(x.Key, "Input", StringComparison.Ordinal)).Value.Value;
        return input == null ? 0 : Convert.ToInt32(input, CultureInfo.InvariantCulture);
    }

    private bool HasAttribute(IMethodSymbol method, string attributeName) => Attribute(method, attributeName) != null;
}
