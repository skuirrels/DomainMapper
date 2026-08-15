using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMapper.Engine;

internal sealed class MapperCompiler
{
    private const string DomainFactoryAttribute = "DomainMapper.Abstractions.DomainFactoryAttribute";
    private const string IgnoreSourceMemberAttribute = "DomainMapper.Abstractions.IgnoreSourceMemberAttribute";
    private const string IgnoreTargetMemberAttribute = "DomainMapper.Abstractions.IgnoreTargetMemberAttribute";
    private const string IncludeMappingAttribute = "DomainMapper.Abstractions.IncludeMappingAttribute";
    private const string MapConditionAttribute = "DomainMapper.Abstractions.MapConditionAttribute";
    private const string MapAfterAttribute = "DomainMapper.Abstractions.MapAfterAttribute";
    private const string MapMemberAttribute = "DomainMapper.Abstractions.MapMemberAttribute";
    private const string MapMaxDepthAttribute = "DomainMapper.Abstractions.MapMaxDepthAttribute";
    private const string MapNullAttribute = "DomainMapper.Abstractions.MapNullAttribute";
    private const string MapNullSubstituteAttribute = "DomainMapper.Abstractions.MapNullSubstituteAttribute";
    private const string MapOnlyTargetMembersAttribute = "DomainMapper.Abstractions.MapOnlyTargetMembersAttribute";
    private const string MapTargetMemberAttribute = "DomainMapper.Abstractions.MapTargetMemberAttribute";
    private const string MapToFactoryAttribute = "DomainMapper.Abstractions.MapToFactoryAttribute";
    private const string MappingCompletenessAttribute = "DomainMapper.Abstractions.MappingCompletenessAttribute";

    private static readonly DiagnosticDescriptor UnsupportedMethod = new(
        "DMPR100",
        "Mapping contract is not supported",
        "Method '{0}' does not match a supported DomainMapper mapping contract",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor CannotConstruct = new(
        "DMPR101",
        "Target cannot be constructed",
        "DomainMapper cannot construct '{0}' from '{1}'",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor InvalidConfiguration = new(
        "DMPR102",
        "Mapping configuration is invalid",
        "Mapping '{0}' configuration is invalid: {1}",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor IncompleteSource = new(
        "DMPR103",
        "Source mapping is incomplete",
        "Mapping '{0}' does not consume or ignore source member '{1}'",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor CompletenessDisabled = new(
        "DMPR104",
        "Mapping completeness is disabled",
        "Mapping '{0}' explicitly disables source and target completeness validation",
        "DomainMapper",
        DiagnosticSeverity.Warning,
        true
    );

    private static readonly SymbolDisplayFormat TypeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
    );

    private readonly INamedTypeSymbol _mapperType;
    private readonly Compilation _compilation;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<MappingContract> _rootContracts = [];
    private readonly List<MappingContract> _helperContracts = [];
    private readonly Queue<MappingRequest> _pendingHelpers = new();
    private readonly Dictionary<string, string> _helperNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedHelperNames = new(StringComparer.Ordinal);
    private readonly HashSet<IMethodSymbol> _activeDomainFactories = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, IReadOnlyList<MappingMember>> _mappingMembers = new(SymbolEqualityComparer.Default);
    private readonly ImmutableArray<IMethodSymbol> _mappingMethods;
    private readonly IReadOnlyDictionary<string, ImmutableArray<IMethodSymbol>> _configurationHelpers;

    private MapperCompiler(INamedTypeSymbol mapperType, Compilation compilation)
    {
        _mapperType = mapperType;
        _compilation = compilation;
        _mappingMethods = mapperType
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(x => x.IsPartialDefinition && x.PartialImplementationPart == null)
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .ToImmutableArray();
        foreach (var memberName in GetTypeHierarchy(mapperType).SelectMany(x => x.GetMembers()).Select(x => x.Name))
        {
            _usedHelperNames.Add(memberName);
        }
        _configurationHelpers = IndexConfigurationHelpers(mapperType);
    }

    public static MapperCompilation Compile(INamedTypeSymbol mapperType, Compilation compilation, CancellationToken cancellationToken) =>
        new MapperCompiler(mapperType, compilation).Build(cancellationToken);

    private MapperCompilation Build(CancellationToken cancellationToken)
    {
        if (GetTypeHierarchy(_mapperType).Any(x => x.IsFileLocal))
        {
            foreach (var method in DiscoverMappingMethods())
            {
                ReportUnsupported(method);
            }

            return new MapperCompilation(BuildHintName(_mapperType), null, _diagnostics.ToImmutableArray());
        }

        ValidateDomainFactories();
        ValidateConfigurationHelpers();

        foreach (var method in DiscoverMappingMethods())
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildRootContract(method);
        }

        while (_pendingHelpers.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildHelperContract(_pendingHelpers.Dequeue());
        }

        var source = _rootContracts.Count == 0 ? null : EmitSource();
        return new MapperCompilation(BuildHintName(_mapperType), source, _diagnostics.ToImmutableArray());
    }

    private ImmutableArray<IMethodSymbol> DiscoverMappingMethods() => _mappingMethods;

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
        var computedMembers = ImmutableDictionary.CreateBuilder<string, IMethodSymbol>(comparer);
        var conditions = ImmutableDictionary.CreateBuilder<string, IMethodSymbol>(comparer);
        var completionHooks = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var completionHookMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var sourceMembers = AllReadableMembers(sourceType);
        var targetMembers = GetAllMappingMembers(targetType).ToArray();

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
            _diagnostics.Add(Diagnostic.Create(CompletenessDisabled, method.Locations.FirstOrDefault(), method.Name));

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
                if (!TryFindMember(targetMembers, memberName, out var member) || !member.CanWrite || member.IsInitOnly)
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

        if (ConfigurationHelpers(method.Name).Any() && DiscoverMappingMethods().Count(x => NamesEqual(x.Name, method.Name)) > 1)
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
                depthExhaustionBehavior
            )
            : null;
    }

    private bool HasExplicitConfiguration(IMethodSymbol method)
    {
        if (_configurationHelpers.ContainsKey(method.Name))
            return true;

        foreach (var attribute in method.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (
                attributeName
                is IgnoreSourceMemberAttribute
                    or IgnoreTargetMemberAttribute
                    or IncludeMappingAttribute
                    or MapMemberAttribute
                    or MapMaxDepthAttribute
                    or MapNullAttribute
                    or MapNullSubstituteAttribute
                    or MapOnlyTargetMembersAttribute
                    or MappingCompletenessAttribute
            )
                return true;
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
            0
        );

    private void BuildRootContract(IMethodSymbol method)
    {
        if (method.ReturnsByRef || method.ReturnsByRefReadonly || method.Parameters.Any(x => x.RefKind == RefKind.Out))
        {
            ReportUnsupported(method);
            return;
        }

        if (method.ReturnsVoid && method.Parameters.Length == 2)
        {
            BuildUpdateContract(method);
            return;
        }

        if (method.ReturnsVoid || method.Parameters.Length == 0)
        {
            ReportUnsupported(method);
            return;
        }

        var sourceParameter = method.Parameters[0];
        var sourceExpression = Escape(sourceParameter.Name);
        var configuration = BuildConfiguration(method, sourceParameter.Type, method.ReturnType, false);
        if (configuration == null)
            return;
        var ambientValues = method.Parameters.Skip(1).Select(x => new MappingValue(x.Name, x.Type, Escape(x.Name))).ToImmutableArray();
        var context = new MappingContext(method.TypeParameters, ambientValues, configuration);
        var factoryName = ReadFactoryName(method);
        IMethodSymbol? selectedFactory = null;

        var expression =
            factoryName == null
                ? BuildRootExpression(sourceParameter.Type, method.ReturnType, sourceExpression, context)
                : BuildFactoryExpression(
                    method.ReturnType,
                    sourceParameter,
                    method.Parameters.Skip(1),
                    factoryName,
                    method,
                    context,
                    out selectedFactory
                );

        if (expression == null)
        {
            if (factoryName == null)
                ReportCannotConstruct(method, sourceParameter.Type, method.ReturnType);
            return;
        }

        var explicitFactoryParameters =
            factoryName == null ? null : method.Parameters.Skip(1).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!ValidateSourceCompleteness(configuration, selectedFactory, explicitFactoryParameters))
            return;

        var hooks = BuildCompletionHooks(configuration, sourceExpression, "target", context);
        if (hooks == null)
            return;
        var guardedHooks = hooks;
        if (hooks.Length > 0 && IsNullable(sourceParameter.Type))
            guardedHooks = $"if ({sourceExpression} is not null && target is not null)\n{{\n{Indent(hooks.TrimEnd())}\n}}\n";
        var body = $"var target = {expression};\n{guardedHooks}return target;";
        _rootContracts.Add(new MappingContract(method.Name, BuildDeclaration(method), body, MappingShape.Create));
    }

    private string? BuildRootExpression(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression, MappingContext context)
    {
        if (TypesEqual(sourceType, targetType))
            return sourceExpression;

        if (sourceType.IsReferenceType && sourceType.NullableAnnotation == NullableAnnotation.Annotated)
            return ConvertExpression(sourceType, targetType, sourceExpression, context);

        if (TryGetDictionaryTypes(sourceType, out _, out _) && TryGetDictionaryTypes(targetType, out _, out _))
            return ConvertExpression(sourceType, targetType, sourceExpression, context);

        if (TryGetSequenceElement(sourceType, out _) && TryGetSequenceElement(targetType, out _))
            return ConvertExpression(sourceType, targetType, sourceExpression, context);

        var objectCreation = BuildObjectCreation(sourceType, targetType, sourceExpression, context);
        if (objectCreation != null || HasTargetConfiguration(RootConfiguration(context, sourceType, targetType)))
            return objectCreation;
        return ConvertExpression(sourceType, targetType, sourceExpression, context);
    }

    [SuppressMessage("Maintainability", "MA0051", Justification = "Keeps null and conversion policy in one semantic planning flow.")]
    private bool TryBuildMemberValue(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        string? targetExpression,
        string targetMemberName,
        ITypeSymbol targetMemberType,
        MappingContext context,
        out string value,
        out string? nullableSourceExpression
    )
    {
        value = string.Empty;
        nullableSourceExpression = null;
        var configuration = RootConfiguration(context, sourceType, targetType);

        MappingMember? sourceMember = null;
        MemberBinding? binding = null;
        string? sourceValueExpression = null;
        ITypeSymbol? sourceValueType = null;
        if (configuration?.Bindings.TryGetValue(targetMemberName, out binding) == true)
        {
            sourceValueExpression = BuildSourcePathExpression(sourceExpression, binding.SourceMembers);
            sourceValueType = EffectivePathType(binding.SourceMembers);
            sourceMember = binding.Leaf;
        }
        else if (TryFindMember(ReadableMembers(sourceType), targetMemberName, out sourceMember))
        {
            sourceValueExpression = $"{sourceExpression}.{Escape(sourceMember.Name)}";
            sourceValueType = sourceMember.Type;
        }

        if (configuration?.ComputedMembers.TryGetValue(targetMemberName, out var computedMethod) == true)
        {
            var call = BuildConfiguredMethodCall(
                computedMethod,
                sourceExpression,
                targetExpression,
                sourceValueExpression,
                sourceMember,
                sourceValueType,
                context
            );
            if (call == null)
            {
                ReportInvalidConfiguration(
                    configuration.Method,
                    $"computed-member method '{computedMethod.Name}' has an unsupported parameter contract"
                );
                return false;
            }
            var converted = ConvertExpression(computedMethod.ReturnType, targetMemberType, call, context);
            if (converted == null)
                return false;
            value = converted;
            return true;
        }

        if (sourceValueExpression == null || sourceValueType == null)
            return false;

        if (configuration?.NullSubstitutes.TryGetValue(targetMemberName, out var substitute) == true && IsNullable(sourceValueType))
        {
            var converted = ConvertExpression(
                NonNullableType(sourceValueType),
                targetMemberType,
                NonNullExpression(sourceValueExpression, sourceValueType),
                context
            );
            if (converted == null)
                return false;
            nullableSourceExpression = sourceValueExpression;
            value = $"{sourceValueExpression} is null ? {substitute} : {converted}";
            return true;
        }

        var behavior =
            configuration?.NullBehaviors.TryGetValue(targetMemberName, out var configuredBehavior) == true ? configuredBehavior : 0;
        if (IsNullable(sourceValueType))
            nullableSourceExpression = sourceValueExpression;

        if (behavior is 1 or 2 or 3 && IsNullable(sourceValueType))
        {
            var nonNullableSource = NonNullableType(sourceValueType);
            var converted = ConvertExpression(
                nonNullableSource,
                targetMemberType,
                NonNullExpression(sourceValueExpression, sourceValueType),
                context
            );
            if (converted == null)
                return false;

            if (behavior == 1)
            {
                value = converted;
                return true;
            }

            if (behavior == 2)
            {
                value =
                    ConvertExpression(
                        nonNullableSource,
                        targetMemberType,
                        $"({sourceValueExpression} ?? throw new global::System.InvalidOperationException(\"Source member for '{Escape(targetMemberName)}' cannot be null.\"))",
                        context
                    ) ?? string.Empty;
                return value.Length > 0;
            }

            var empty = BuildEmptyCollectionExpression(targetMemberType);
            if (empty == null)
            {
                ReportInvalidConfiguration(
                    configuration!.Method,
                    $"EmptyCollection null behavior for '{targetMemberName}' requires a supported collection target"
                );
                return false;
            }
            value = $"{sourceValueExpression} is null ? {empty} : {converted}";
            return true;
        }

        var regularValue = ConvertExpression(sourceValueType, targetMemberType, sourceValueExpression, context);
        if (regularValue == null)
            return false;
        value = regularValue;
        return true;
    }

    private string? BuildConfiguredMethodCall(
        IMethodSymbol helper,
        string sourceExpression,
        string? targetExpression,
        string? sourceMemberExpression,
        MappingMember? sourceMember,
        ITypeSymbol? sourceMemberType,
        MappingContext context
    )
    {
        var configuration = context.Configuration!;
        var sourceParameterName = configuration.Method.Parameters[0].Name;
        var targetParameterName =
            configuration.Method.ReturnsVoid && configuration.Method.Parameters.Length > 1
                ? configuration.Method.Parameters[1].Name
                : "target";
        var arguments = new List<string>();
        foreach (var parameter in helper.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
                return null;

            if (MatchesRootType(parameter.Type, configuration.SourceType) && NamesEqual(parameter.Name, sourceParameterName))
            {
                arguments.Add(sourceExpression);
                continue;
            }

            if (
                targetExpression != null
                && MatchesRootType(parameter.Type, configuration.TargetType)
                && NamesEqual(parameter.Name, targetParameterName)
            )
            {
                arguments.Add(targetExpression);
                continue;
            }

            if (TryFindValue(context.AmbientValues, parameter.Name, out var ambient) && TypesEqual(ambient.Type, parameter.Type))
            {
                arguments.Add(ambient.Expression);
                continue;
            }

            if (
                sourceMember != null
                && sourceMemberExpression != null
                && sourceMemberType != null
                && TypesEqual(sourceMemberType, parameter.Type)
                && (NamesEqual(parameter.Name, sourceMember.Name) || NamesEqual(parameter.Name, "value"))
            )
            {
                arguments.Add(sourceMemberExpression);
                continue;
            }

            if (
                MatchesRootType(parameter.Type, configuration.SourceType)
                && helper.Parameters.Count(x => MatchesRootType(x.Type, configuration.SourceType)) == 1
            )
            {
                arguments.Add(sourceExpression);
                continue;
            }

            if (
                targetExpression != null
                && MatchesRootType(parameter.Type, configuration.TargetType)
                && helper.Parameters.Count(x => MatchesRootType(x.Type, configuration.TargetType)) == 1
            )
            {
                arguments.Add(targetExpression);
                continue;
            }

            return null;
        }

        return $"{Escape(helper.Name)}({string.Join(", ", arguments)})";
    }

    private static bool MatchesRootType(ITypeSymbol candidate, ITypeSymbol configuredType) =>
        TypesEqual(candidate, configuredType) || IsNullable(configuredType) && TypesEqual(candidate, NonNullableType(configuredType));

    private static string Indent(string value) => "    " + value.Replace("\n", "\n    ");

    private string? BuildConditionExpression(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        string? targetExpression,
        string targetMemberName,
        MappingContext context
    )
    {
        var configuration = RootConfiguration(context, sourceType, targetType);
        if (configuration?.Conditions.TryGetValue(targetMemberName, out var condition) != true)
            return null;

        MappingMember? sourceMember = null;
        string? sourceMemberExpression = null;
        ITypeSymbol? sourceMemberType = null;
        if (configuration.Bindings.TryGetValue(targetMemberName, out var binding))
        {
            sourceMember = binding.Leaf;
            sourceMemberExpression = BuildSourcePathExpression(sourceExpression, binding.SourceMembers);
            sourceMemberType = EffectivePathType(binding.SourceMembers);
        }
        else if (TryFindMember(ReadableMembers(sourceType), targetMemberName, out sourceMember))
        {
            sourceMemberExpression = $"{sourceExpression}.{Escape(sourceMember.Name)}";
            sourceMemberType = sourceMember.Type;
        }

        var call = BuildConfiguredMethodCall(
            condition!,
            sourceExpression,
            targetExpression,
            sourceMemberExpression,
            sourceMember,
            sourceMemberType,
            context
        );
        if (call == null)
            ReportInvalidConfiguration(
                configuration!.Method,
                $"condition method '{condition!.Name}' has an unsupported parameter contract"
            );
        return call;
    }

    private string? BuildCompletionHooks(
        MappingMethodConfiguration configuration,
        string sourceExpression,
        string targetExpression,
        MappingContext context
    )
    {
        if (configuration.CompletionHooks.Length == 0)
            return string.Empty;

        var lines = new List<string>();
        foreach (var hook in configuration.CompletionHooks)
        {
            var call = BuildConfiguredMethodCall(hook, sourceExpression, targetExpression, null, null, null, context);
            if (call == null)
            {
                ReportInvalidConfiguration(configuration.Method, $"completion hook '{hook.Name}' has an unsupported parameter contract");
                return null;
            }
            lines.Add(call + ";");
        }
        return string.Join("\n", lines) + "\n";
    }

    private void BuildUpdateContract(IMethodSymbol method)
    {
        var source = method.Parameters[0];
        var target = method.Parameters[1];
        if (
            IsNullable(source.Type)
            || IsNullable(target.Type)
            || target.RefKind is not (RefKind.None or RefKind.Ref)
            || target.Type.IsValueType && target.RefKind != RefKind.Ref
        )
        {
            ReportUnsupported(method);
            return;
        }

        var configuration = BuildConfiguration(method, source.Type, target.Type, true);
        if (configuration == null)
            return;
        var context = new MappingContext(method.TypeParameters, ImmutableArray<MappingValue>.Empty, configuration);
        if (
            !TryBuildAssignments(
                source.Type,
                target.Type,
                Escape(source.Name),
                Escape(target.Name),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                true,
                context,
                out var assignments
            )
        )
        {
            ReportCannotConstruct(method, source.Type, target.Type);
            return;
        }

        if (!ValidateSourceCompleteness(configuration, null, null))
            return;

        var hooks = BuildCompletionHooks(configuration, Escape(source.Name), Escape(target.Name), context);
        if (hooks == null)
            return;

        _rootContracts.Add(
            new MappingContract(
                method.Name,
                BuildDeclaration(method),
                assignments + (hooks.Length == 0 ? string.Empty : "\n" + hooks.TrimEnd()),
                MappingShape.Update
            )
        );
    }

    private void BuildHelperContract(MappingRequest request)
    {
        var expression = BuildObjectCreation(request.SourceType, request.TargetType, "source", request.Context);
        if (expression == null)
        {
            _diagnostics.Add(
                Diagnostic.Create(
                    CannotConstruct,
                    _mapperType.Locations.FirstOrDefault(),
                    request.TargetType.ToDisplayString(),
                    request.SourceType.ToDisplayString()
                )
            );
            return;
        }

        var declaration = BuildHelperDeclaration(request.TargetType, request.MethodName, request.SourceType, request.Context);
        var depthGuard = BuildDepthGuard(request.TargetType, request.Context);
        _helperContracts.Add(
            new MappingContract(
                request.MethodName,
                declaration,
                $"{depthGuard}var target = {expression};\nreturn target;",
                MappingShape.Helper
            )
        );
    }

    private static string BuildDepthGuard(ITypeSymbol targetType, MappingContext context)
    {
        if (context.Configuration?.MaximumDepth == null)
            return string.Empty;
        return context.Configuration.DepthExhaustionBehavior == 1
            ? "if (__depth <= 0)\n{\n    throw new global::System.InvalidOperationException(\"DomainMapper maximum mapping depth was exhausted.\");\n}\n"
            : $"if (__depth <= 0)\n{{\n    return default({TypeName(targetType)})!;\n}}\n";
    }

    private string? ConvertExpression(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression, MappingContext context)
    {
        if (TypesEqual(sourceType, targetType))
            return sourceExpression;

        var domainConversion = BuildDomainConversionExpression(sourceType, targetType, sourceExpression, context);
        if (domainConversion != null)
            return domainConversion;

        if (targetType.IsReferenceType && targetType.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var nonNullableTarget = targetType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            if (sourceType.IsReferenceType && sourceType.NullableAnnotation == NullableAnnotation.Annotated)
            {
                var nonNullableSource = sourceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                var nullableExpression = ConvertExpression(nonNullableSource, nonNullableTarget, sourceExpression, context);
                return nullableExpression == null ? null : $"{sourceExpression} is null ? null : {nullableExpression}";
            }

            return ConvertExpression(sourceType, nonNullableTarget, sourceExpression, context);
        }

        if (sourceType.IsReferenceType && sourceType.NullableAnnotation == NullableAnnotation.Annotated)
            return null;

        if (
            TryGetDictionaryTypes(sourceType, out var sourceKey, out var sourceValue)
            && TryGetDictionaryTypes(targetType, out var targetKey, out var targetValue)
        )
        {
            return BuildDictionaryConversion(
                sourceType,
                targetType,
                sourceKey,
                sourceValue,
                targetKey,
                targetValue,
                sourceExpression,
                context
            );
        }

        if (TryGetSequenceElement(sourceType, out var sourceElement) && TryGetSequenceElement(targetType, out var targetElement))
            return BuildSequenceConversion(sourceType, targetType, sourceElement, targetElement, sourceExpression, context);

        var conversion = _compilation.ClassifyConversion(sourceType, targetType);
        if (conversion.Exists && conversion.IsImplicit)
            return sourceExpression;

        if (targetType is INamedTypeSymbol namedTarget)
        {
            var singleValueConstructor = FindSingleValueConstructor(sourceType, namedTarget);
            if (singleValueConstructor != null)
            {
                var consumedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { singleValueConstructor.Parameters[0].Name };
                if (CanUseScalarConstructor(namedTarget, singleValueConstructor, consumedMembers))
                    return $"new {TypeName(targetType)}({sourceExpression})";
            }
        }

        return QueueObjectHelper(sourceType, targetType, sourceExpression, context);
    }

    private string? BuildObjectCreation(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression, MappingContext context)
    {
        if (
            targetType is not INamedTypeSymbol { SpecialType: SpecialType.None, TypeKind: TypeKind.Class or TypeKind.Struct } namedTarget
            || namedTarget.IsAbstract
        )
            return null;

        var constructors = namedTarget
            .InstanceConstructors.Where(IsAccessible)
            .Where(x => !IsRecordCopyConstructor(x, namedTarget))
            .OrderByDescending(x => x.Parameters.Length)
            .ToArray();

        foreach (var constructor in constructors.Where(x => x.Parameters.Length > 0))
        {
            var creation = BuildConstructorCreation(sourceType, targetType, sourceExpression, constructor, context);
            if (creation != null)
                return creation;
        }

        var parameterlessConstructor = constructors.FirstOrDefault(x => x.Parameters.Length == 0);
        if (parameterlessConstructor != null)
        {
            if (
                !TryBuildCreationPlan(
                    sourceType,
                    targetType,
                    sourceExpression,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    parameterlessConstructor,
                    context,
                    out var initializer,
                    out var assignments
                )
            )
                return null;

            var creation = $"new {TypeName(targetType)}(){initializer}";
            return assignments.Length == 0 ? creation : new DeferredObjectCreation(creation, assignments).ToMarker();
        }

        return null;
    }

    private string? BuildConstructorCreation(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        IMethodSymbol constructor,
        MappingContext context
    )
    {
        var arguments = new List<string>();
        var consumedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuration = RootConfiguration(context, sourceType, targetType);
        foreach (var parameter in constructor.Parameters)
        {
            if (
                configuration?.IgnoredTargets.Contains(parameter.Name) == true
                || configuration?.Conditions.ContainsKey(parameter.Name) == true
            )
                return null;
            if (
                !TryBuildMemberValue(
                    sourceType,
                    targetType,
                    sourceExpression,
                    null,
                    parameter.Name,
                    parameter.Type,
                    context,
                    out var argument,
                    out _
                )
            )
                return null;

            arguments.Add(argument);
            consumedMembers.Add(parameter.Name);
        }

        if (
            !TryBuildCreationPlan(
                sourceType,
                targetType,
                sourceExpression,
                consumedMembers,
                constructor,
                context,
                out var initializer,
                out var assignments
            )
        )
            return null;

        var creation = $"new {TypeName(targetType)}({string.Join(", ", arguments)}){initializer}";
        return assignments.Length == 0 ? creation : new DeferredObjectCreation(creation, assignments).ToMarker();
    }

    private bool TryBuildAssignments(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        string targetExpression,
        ISet<string> consumedMembers,
        bool requireAssignment,
        MappingContext context,
        out string assignments
    )
    {
        var sourceMembers = ReadableMembers(sourceType);
        var configuration = RootConfiguration(context, sourceType, targetType);
        var writableMembers = WritableTargetMembers(targetType, configuration);
        foreach (var targetMember in ReadableTargetMembers(targetType, configuration))
        {
            if (configuration?.IgnoredTargets.Contains(targetMember.Name) == true)
                continue;
            if (configuration?.OnlyTargets != null && !configuration.OnlyTargets.Contains(targetMember.Name))
                continue;
            if (
                !consumedMembers.Contains(targetMember.Name)
                && HasConfiguredOrConventionValue(configuration, sourceMembers, targetMember.Name)
                && !writableMembers.Any(x => SymbolEqualityComparer.Default.Equals(x.Symbol, targetMember.Symbol))
            )
            {
                assignments = string.Empty;
                return false;
            }
        }

        var lines = new List<string>();
        foreach (var targetMember in writableMembers)
        {
            if (consumedMembers.Contains(targetMember.Name))
                continue;
            if (configuration?.IgnoredTargets.Contains(targetMember.Name) == true)
                continue;
            if (configuration?.OnlyTargets != null && !configuration.OnlyTargets.Contains(targetMember.Name))
                continue;

            if (
                !TryBuildMemberValue(
                    sourceType,
                    targetType,
                    sourceExpression,
                    targetExpression,
                    targetMember.Name,
                    targetMember.Type,
                    context,
                    out var value,
                    out var nullableSourceExpression
                )
            )
            {
                if (configuration?.EnforceTarget == false)
                    continue;
                assignments = string.Empty;
                return false;
            }

            var assignment = $"{targetExpression}.{Escape(targetMember.Name)} = {value};";
            var condition = BuildConditionExpression(
                sourceType,
                targetType,
                sourceExpression,
                targetExpression,
                targetMember.Name,
                context
            );
            if (configuration?.NullBehaviors.TryGetValue(targetMember.Name, out var behavior) == true && behavior == 1)
                condition =
                    condition == null
                        ? $"{nullableSourceExpression} is not null"
                        : $"{nullableSourceExpression} is not null && {condition}";
            lines.Add(condition == null ? assignment : $"if ({condition})\n{{\n    {assignment}\n}}");
        }

        assignments = string.Join("\n", lines);
        return !requireAssignment || lines.Count > 0;
    }

    [SuppressMessage("Maintainability", "MA0051", Justification = "Keeps fail-closed construction planning in one flow.")]
    private bool TryBuildCreationPlan(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        ISet<string> consumedMembers,
        IMethodSymbol constructor,
        MappingContext context,
        out string initializer,
        out string assignments
    )
    {
        var sourceMembers = ReadableMembers(sourceType);
        var configuration = RootConfiguration(context, sourceType, targetType);
        var settableMembers = SettableTargetMembers(targetType, configuration);
        var constructorSetsRequiredMembers = SetsRequiredMembers(constructor);
        if (!constructorSetsRequiredMembers && RequiredFields(targetType).Count > 0)
        {
            initializer = string.Empty;
            assignments = string.Empty;
            return false;
        }

        foreach (var targetMember in ReadableTargetMembers(targetType, configuration))
        {
            if (configuration?.IgnoredTargets.Contains(targetMember.Name) == true)
                continue;
            var requiresInitializer = targetMember.IsRequired && !constructorSetsRequiredMembers;
            if (
                (!consumedMembers.Contains(targetMember.Name) || requiresInitializer)
                && HasConfiguredOrConventionValue(configuration, sourceMembers, targetMember.Name)
                && !settableMembers.Any(x => SymbolEqualityComparer.Default.Equals(x.Symbol, targetMember.Symbol))
            )
            {
                initializer = string.Empty;
                assignments = string.Empty;
                return false;
            }
        }

        var initializerEntries = new List<string>();
        var assignmentLines = new List<string>();
        foreach (var targetMember in settableMembers)
        {
            var requiresRequiredMemberInitializer = targetMember.IsRequired && !constructorSetsRequiredMembers;
            if (consumedMembers.Contains(targetMember.Name) && !requiresRequiredMemberInitializer)
                continue;
            if (configuration?.IgnoredTargets.Contains(targetMember.Name) == true)
            {
                if (requiresRequiredMemberInitializer)
                {
                    initializer = string.Empty;
                    assignments = string.Empty;
                    return false;
                }
                continue;
            }

            var requiresInitializer = targetMember.IsInitOnly || requiresRequiredMemberInitializer;

            if (
                !TryBuildMemberValue(
                    sourceType,
                    targetType,
                    sourceExpression,
                    requiresInitializer ? null : "target",
                    targetMember.Name,
                    targetMember.Type,
                    context,
                    out var value,
                    out _
                )
            )
            {
                if (configuration?.EnforceTarget == false && !requiresRequiredMemberInitializer)
                    continue;
                initializer = string.Empty;
                assignments = string.Empty;
                return false;
            }

            var condition = BuildConditionExpression(sourceType, targetType, sourceExpression, "target", targetMember.Name, context);
            if (requiresInitializer && condition != null)
            {
                initializer = string.Empty;
                assignments = string.Empty;
                return false;
            }

            if (requiresInitializer)
                initializerEntries.Add($"{Escape(targetMember.Name)} = {value}");
            else
            {
                var assignment = $"target.{Escape(targetMember.Name)} = {value};";
                assignmentLines.Add(condition == null ? assignment : $"if ({condition})\n{{\n    {assignment}\n}}");
            }
        }

        initializer = initializerEntries.Count == 0 ? string.Empty : $" {{ {string.Join(", ", initializerEntries)} }}";
        assignments = string.Join("\n", assignmentLines);
        return true;
    }

    private string? BuildFactoryExpression(
        ITypeSymbol targetType,
        IParameterSymbol sourceParameter,
        IEnumerable<IParameterSymbol> additionalParameters,
        string factoryName,
        IMethodSymbol mappingMethod,
        MappingContext context,
        out IMethodSymbol? selectedFactory
    )
    {
        selectedFactory = null;
        if (targetType is not INamedTypeSymbol namedTarget)
        {
            ReportCannotConstruct(mappingMethod, sourceParameter.Type, targetType);
            return null;
        }

        var explicitValues = additionalParameters.Select(x => new MappingValue(x.Name, x.Type, Escape(x.Name))).ToArray();
        var availableValues = explicitValues
            .Concat(
                ReadableMembers(sourceParameter.Type)
                    .Where(x => !explicitValues.Any(y => NamesEqual(x.Name, y.Name)))
                    .Select(x => new MappingValue(x.Name, x.Type, $"{Escape(sourceParameter.Name)}.{Escape(x.Name)}"))
            )
            .ToArray();
        var factoryContext = context.WithAmbient(availableValues);

        foreach (
            var factory in GetAllMethods(namedTarget, factoryName)
                .Where(x =>
                    x.IsStatic
                    && IsAccessible(x)
                    && x.TypeParameters.Length == 0
                    && TypesEqual(x.ReturnType, targetType)
                    && x.Parameters.All(y => y.RefKind == RefKind.None)
                )
                .OrderByDescending(x => x.Parameters.Length)
        )
        {
            var arguments = new List<string>();
            var valid = true;
            foreach (var parameter in factory.Parameters)
            {
                string? argument;
                if (TryFindValue(explicitValues, parameter.Name, out var explicitValue))
                {
                    argument = ConvertExpression(explicitValue.Type, parameter.Type, explicitValue.Expression, factoryContext);
                }
                else
                {
                    argument = TryBuildMemberValue(
                        sourceParameter.Type,
                        targetType,
                        Escape(sourceParameter.Name),
                        null,
                        parameter.Name,
                        parameter.Type,
                        factoryContext,
                        out var configuredArgument,
                        out _
                    )
                        ? configuredArgument
                        : null;
                }
                if (argument == null)
                {
                    valid = false;
                    break;
                }

                arguments.Add(argument);
            }

            if (valid)
            {
                selectedFactory = factory;
                return $"{TypeName(targetType)}.{Escape(factory.Name)}({string.Join(", ", arguments)})";
            }
        }

        ReportCannotConstruct(mappingMethod, sourceParameter.Type, targetType);
        return null;
    }

    private string? BuildSequenceConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ITypeSymbol sourceElement,
        ITypeSymbol targetElement,
        string sourceExpression,
        MappingContext context
    )
    {
        if (!CanCreateSequenceTarget(targetType))
            return null;

        var helperContext = context.ForHelper();
        var elementExpression = ConvertExpression(sourceElement, targetElement, "item", helperContext);
        if (elementExpression == null)
            return null;

        var count = CountExpression(sourceType);
        var creation = targetType is IArrayTypeSymbol ? null : BuildSequenceCreation(targetType, targetElement, count);
        if (targetType is not IArrayTypeSymbol && creation == null)
            return null;

        var key = BuildHelperKey(sourceType, targetType, context);
        var isNew = ReserveHelper(key, $"MapTo{SequenceName(targetType, targetElement)}", out var helperName);
        if (isNew)
        {
            if (targetType is IArrayTypeSymbol)
            {
                string body;
                if (count == null)
                {
                    body =
                        $"var target = new global::System.Collections.Generic.List<{TypeName(targetElement)}>();\n"
                        + $"foreach (var item in {EnumerableExpression(sourceType, sourceElement, "source")})\n{{\n    target.Add({elementExpression});\n}}\n"
                        + "return target.ToArray();";
                }
                else if (IndexExpression(sourceType, "source", "i") is { } indexedItem)
                {
                    body =
                        $"var target = new {TypeName(targetElement)}[{count}];\n"
                        + $"for (var i = 0; i < {count}; i++)\n{{\n    var item = {indexedItem};\n    target[i] = {elementExpression};\n}}\n"
                        + "return target;";
                }
                else
                {
                    body =
                        $"var target = new {TypeName(targetElement)}[{count}];\n"
                        + $"var index = 0;\nforeach (var item in {EnumerableExpression(sourceType, sourceElement, "source")})\n{{\n    target[index++] = {elementExpression};\n}}\n"
                        + "return target;";
                }

                _helperContracts.Add(
                    new MappingContract(
                        helperName,
                        BuildHelperDeclaration(targetType, helperName, sourceType, helperContext),
                        body,
                        MappingShape.Helper
                    )
                );
                return BuildHelperCall(helperName, sourceExpression, context);
            }

            var iteration = IndexExpression(sourceType, "source", "i") is { } indexedItemExpression
                ? $"for (var i = 0; i < {count}; i++)\n{{\n    var item = {indexedItemExpression};\n    target.Add({elementExpression});\n}}"
                : $"foreach (var item in {EnumerableExpression(sourceType, sourceElement, "source")})\n{{\n    target.Add({elementExpression});\n}}";
            var declaration = BuildHelperDeclaration(targetType, helperName, sourceType, helperContext);
            _helperContracts.Add(
                new MappingContract(helperName, declaration, $"var target = {creation};\n{iteration}\nreturn target;", MappingShape.Helper)
            );
        }

        return BuildHelperCall(helperName, sourceExpression, context);
    }

    private string? BuildDictionaryConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ITypeSymbol sourceKey,
        ITypeSymbol sourceValue,
        ITypeSymbol targetKey,
        ITypeSymbol targetValue,
        string sourceExpression,
        MappingContext context
    )
    {
        var creationType = DictionaryCreationType(targetType, targetKey, targetValue);
        if (creationType == null)
            return null;

        var helperContext = context.ForHelper();
        var keyExpression = ConvertExpression(sourceKey, targetKey, "item.Key", helperContext);
        var valueExpression = ConvertExpression(sourceValue, targetValue, "item.Value", helperContext);
        if (keyExpression == null || valueExpression == null)
            return null;

        var key = BuildHelperKey(sourceType, targetType, context);
        var isNew = ReserveHelper(key, $"MapToDictionaryOf{Sanitize(targetKey.Name)}And{Sanitize(targetValue.Name)}", out var helperName);
        if (isNew)
        {
            var declaration = BuildHelperDeclaration(targetType, helperName, sourceType, helperContext);
            var body =
                $"var target = new {creationType}({DictionaryCountExpression(sourceType, "source")});\n"
                + $"foreach (var item in {DictionaryExpression(sourceType, sourceKey, sourceValue, "source")})\n{{\n    target[{keyExpression}] = {valueExpression};\n}}\nreturn target;";
            _helperContracts.Add(new MappingContract(helperName, declaration, body, MappingShape.Helper));
        }

        return BuildHelperCall(helperName, sourceExpression, context);
    }

    private string? QueueObjectHelper(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression, MappingContext context)
    {
        var configuration = RootConfiguration(context, sourceType, targetType);
        if (
            !HasTargetConfiguration(configuration)
            && !CanConstructObject(sourceType, targetType, context, new HashSet<string>(StringComparer.Ordinal))
        )
            return null;

        var key = BuildHelperKey(sourceType, targetType, context);
        if (ReserveHelper(key, $"MapTo{Sanitize(targetType.Name)}", out var helperName))
            _pendingHelpers.Enqueue(new MappingRequest(sourceType, targetType, helperName, context.ForHelper()));
        return BuildHelperCall(helperName, sourceExpression, context);
    }

    private static bool HasTargetConfiguration(MappingMethodConfiguration? configuration) =>
        configuration != null
        && (
            configuration.Bindings.Count > 0
            || configuration.IgnoredTargets.Count > 0
            || configuration.OnlyTargets != null
            || configuration.NullBehaviors.Count > 0
            || configuration.NullSubstitutes.Count > 0
            || configuration.ComputedMembers.Count > 0
            || configuration.Conditions.Count > 0
            || !configuration.EnforceTarget
        );

    private string? BuildDomainConversionExpression(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        MappingContext context
    )
    {
        foreach (var method in DomainFactoryMethods(targetType))
        {
            if (!_activeDomainFactories.Add(method))
                continue;

            try
            {
                if (ReadDomainFactoryInput(method) == 1)
                {
                    if (method.Parameters is [{ RefKind: RefKind.None } parameter] && TypesEqual(parameter.Type, sourceType))
                        return $"{Escape(method.Name)}({sourceExpression})";
                    continue;
                }

                var sourceValues = ReadableMembers(sourceType)
                    .Select(x => new MappingValue(x.Name, x.Type, $"{sourceExpression}.{Escape(x.Name)}"))
                    .ToArray();
                var availableValues = sourceValues
                    .Concat(context.AmbientValues.Where(x => !sourceValues.Any(y => NamesEqual(x.Name, y.Name))))
                    .ToArray();
                var factoryContext = context.WithAmbient(availableValues);
                var arguments = new List<string>();
                var valid = true;
                foreach (var parameter in method.Parameters)
                {
                    if (parameter.RefKind != RefKind.None || !TryFindValue(availableValues, parameter.Name, out var availableValue))
                    {
                        valid = false;
                        break;
                    }

                    var argument = ConvertExpression(availableValue.Type, parameter.Type, availableValue.Expression, factoryContext);
                    if (argument == null)
                    {
                        valid = false;
                        break;
                    }

                    arguments.Add(argument);
                }

                if (valid)
                    return $"{Escape(method.Name)}({string.Join(", ", arguments)})";
            }
            finally
            {
                _activeDomainFactories.Remove(method);
            }
        }

        return null;
    }

    private IEnumerable<IMethodSymbol> DomainFactoryMethods(ITypeSymbol targetType) =>
        _mapperType
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(x =>
                x.IsStatic
                && x.TypeParameters.Length == 0
                && IsAccessible(x)
                && HasAttribute(x, DomainFactoryAttribute)
                && TypesEqual(x.ReturnType, targetType)
            )
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue);

    private void ValidateDomainFactories()
    {
        foreach (var method in _mapperType.GetMembers().OfType<IMethodSymbol>().Where(x => HasAttribute(x, DomainFactoryAttribute)))
        {
            var sourceInputIsValid = ReadDomainFactoryInput(method) != 1 || method.Parameters is [{ RefKind: RefKind.None }];
            if (!method.IsStatic || method.ReturnsVoid || method.TypeParameters.Length > 0 || !sourceInputIsValid)
                ReportUnsupported(method);
        }
    }

    private bool CanConstructObject(ITypeSymbol sourceType, ITypeSymbol targetType, MappingContext context, ISet<string> visiting)
    {
        var key = BuildHelperKey(sourceType, targetType, context);
        if (!visiting.Add(key))
            return context.Configuration?.MaximumDepth != null;

        try
        {
            if (
                targetType
                    is not INamedTypeSymbol { SpecialType: SpecialType.None, TypeKind: TypeKind.Class or TypeKind.Struct } namedTarget
                || namedTarget.IsAbstract
            )
                return false;

            var sourceMembers = ReadableMembers(sourceType);
            foreach (
                var constructor in namedTarget
                    .InstanceConstructors.Where(IsAccessible)
                    .Where(x => !IsRecordCopyConstructor(x, namedTarget))
                    .OrderByDescending(x => x.Parameters.Length)
            )
            {
                var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var constructorValid = true;
                foreach (var parameter in constructor.Parameters)
                {
                    if (
                        !TryFindMember(sourceMembers, parameter.Name, out var sourceMember)
                        || !CanConvert(sourceMember.Type, parameter.Type, context, visiting)
                    )
                    {
                        constructorValid = false;
                        break;
                    }
                    consumed.Add(parameter.Name);
                }

                if (!constructorValid)
                    continue;

                var constructorSetsRequiredMembers = SetsRequiredMembers(constructor);
                if (!constructorSetsRequiredMembers && RequiredFields(targetType).Count > 0)
                    continue;

                var settableMembers = SettableMembers(targetType);
                var assignmentsValid = settableMembers
                    .Where(x => !consumed.Contains(x.Name) || (x.IsRequired && !constructorSetsRequiredMembers))
                    .All(x =>
                        TryFindMember(sourceMembers, x.Name, out var sourceMember)
                        && CanConvert(sourceMember.Type, x.Type, context, visiting)
                    );
                var inaccessibleStateIsSafe = ReadableMembers(targetType)
                    .All(x =>
                        (consumed.Contains(x.Name) && (!x.IsRequired || constructorSetsRequiredMembers))
                        || !TryFindMember(sourceMembers, x.Name, out _)
                        || settableMembers.Any(y => SymbolEqualityComparer.Default.Equals(x.Symbol, y.Symbol))
                    );
                if (assignmentsValid && inaccessibleStateIsSafe)
                    return true;
            }

            return false;
        }
        finally
        {
            visiting.Remove(key);
        }
    }

    private bool CanConvert(ITypeSymbol sourceType, ITypeSymbol targetType, MappingContext context, ISet<string> visiting)
    {
        if (TypesEqual(sourceType, targetType))
            return true;

        if (CanUseDomainFactory(sourceType, targetType, context, visiting))
            return true;

        if (targetType.IsReferenceType && targetType.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var nonNullableTarget = targetType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var nonNullableSource = sourceType.IsReferenceType
                ? sourceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                : sourceType;
            return CanConvert(nonNullableSource, nonNullableTarget, context, visiting);
        }

        if (sourceType.IsReferenceType && sourceType.NullableAnnotation == NullableAnnotation.Annotated)
            return false;

        if (
            TryGetDictionaryTypes(sourceType, out var sourceKey, out var sourceValue)
            && TryGetDictionaryTypes(targetType, out var targetKey, out var targetValue)
        )
        {
            return DictionaryCreationType(targetType, targetKey, targetValue) != null
                && CanConvert(sourceKey, targetKey, context, visiting)
                && CanConvert(sourceValue, targetValue, context, visiting);
        }

        if (TryGetSequenceElement(sourceType, out var sourceElement) && TryGetSequenceElement(targetType, out var targetElement))
            return CanCreateSequenceTarget(targetType) && CanConvert(sourceElement, targetElement, context, visiting);

        var conversion = _compilation.ClassifyConversion(sourceType, targetType);
        if (conversion.Exists && conversion.IsImplicit)
            return true;

        if (
            targetType is INamedTypeSymbol namedTarget
            && FindSingleValueConstructor(sourceType, namedTarget) is { } singleValueConstructor
            && CanUseScalarConstructor(
                namedTarget,
                singleValueConstructor,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { singleValueConstructor.Parameters[0].Name }
            )
        )
        {
            return true;
        }

        return CanConstructObject(sourceType, targetType, context, visiting);
    }

    private bool CanUseDomainFactory(ITypeSymbol sourceType, ITypeSymbol targetType, MappingContext context, ISet<string> visiting)
    {
        foreach (var method in DomainFactoryMethods(targetType))
        {
            if (ReadDomainFactoryInput(method) == 1)
            {
                if (method.Parameters is [{ RefKind: RefKind.None } parameter] && TypesEqual(parameter.Type, sourceType))
                    return true;
                continue;
            }

            var sourceValues = ReadableMembers(sourceType).Select(x => new MappingValue(x.Name, x.Type, string.Empty)).ToArray();
            var availableValues = sourceValues
                .Concat(context.AmbientValues.Where(x => !sourceValues.Any(y => NamesEqual(x.Name, y.Name))))
                .ToArray();
            if (
                method.Parameters.All(x =>
                    x.RefKind == RefKind.None
                    && TryFindValue(availableValues, x.Name, out var sourceValue)
                    && CanConvert(sourceValue.Type, x.Type, context.WithAmbient(availableValues), visiting)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private string EmitSource()
    {
        var writer = new SourceWriter();
        writer.Line("// <auto-generated />");
        writer.Line("#nullable enable");

        var namespaceName = _mapperType.ContainingNamespace.IsGlobalNamespace ? null : _mapperType.ContainingNamespace.ToDisplayString();
        if (namespaceName != null)
        {
            writer.Line($"namespace {namespaceName}");
            writer.Line("{");
            writer.Indent();
        }

        var typeHierarchy = GetTypeHierarchy(_mapperType).ToArray();
        foreach (var type in typeHierarchy)
        {
            writer.Line(BuildTypeDeclaration(type));
            writer.Line("{");
            writer.Indent();
        }

        var contracts = _rootContracts.Concat(_helperContracts).ToArray();
        for (var index = 0; index < contracts.Length; index++)
        {
            EmitContract(writer, contracts[index]);
            if (index < contracts.Length - 1)
                writer.Line();
        }

        foreach (var _ in typeHierarchy)
        {
            writer.Unindent();
            writer.Line("}");
        }

        if (namespaceName != null)
        {
            writer.Unindent();
            writer.Line("}");
        }

        return writer.ToString();
    }

    private static void EmitContract(SourceWriter writer, MappingContract contract)
    {
        writer.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"DomainMapper\", \"0.0.1.0\")]");
        writer.Line(contract.Declaration);
        writer.Line("{");
        writer.Indent();
        foreach (var line in ExpandDeferredCreation(contract.Body).Split('\n'))
        {
            writer.Line(line);
        }

        writer.Unindent();
        writer.Line("}");
    }

    private static string ExpandDeferredCreation(string body)
    {
        const string markerPrefix = "__DOMAINMAPPER_CREATE__(";
        var markerStart = body.IndexOf(markerPrefix, StringComparison.Ordinal);
        if (markerStart < 0)
            return body;

        var markerEnd = body.IndexOf(")__", markerStart, StringComparison.Ordinal);
        if (markerEnd < 0)
            return body;

        var payload = body.Substring(markerStart + markerPrefix.Length, markerEnd - markerStart - markerPrefix.Length);
        var separator = payload.IndexOf('|');
        if (separator < 0)
            return body;

        string creation;
        string assignments;
        try
        {
            creation = Encoding.UTF8.GetString(Convert.FromBase64String(payload.Substring(0, separator)));
            assignments = Encoding.UTF8.GetString(Convert.FromBase64String(payload.Substring(separator + 1)));
        }
        catch (FormatException)
        {
            return body;
        }
        var statementStart = body.LastIndexOf("var target = ", markerStart, StringComparison.Ordinal);
        var statementEnd = body.IndexOf(';', markerEnd + 3);
        if (statementStart < 0 || statementEnd < 0)
            return body;
        return body.Substring(0, statementStart) + $"var target = {creation};\n{assignments}" + body.Substring(statementEnd + 1);
    }

    private string BuildDeclaration(IMethodSymbol method)
    {
        var modifiers = new List<string> { AccessibilityText(method.DeclaredAccessibility) };
        if (HasExplicitNewModifier(method))
            modifiers.Add("new");
        if (method.IsStatic)
            modifiers.Add("static");
        else
        {
            if (method.IsSealed)
                modifiers.Add("sealed");
            if (method.IsOverride)
                modifiers.Add("override");
            else if (method.IsVirtual)
                modifiers.Add("virtual");
        }
        if (RequiresUnsafeModifier(method))
            modifiers.Add("unsafe");
        modifiers.Add("partial");

        var typeParameters = TypeParameters(method.TypeParameters);
        var parameters = string.Join(", ", method.Parameters.Select(ParameterDeclaration));
        var constraints = ConstraintClauses(method.TypeParameters);
        return $"{string.Join(" ", modifiers)} {TypeName(method.ReturnType)} {Escape(method.Name)}{typeParameters}({parameters}){constraints}";
    }

    private static bool HasExplicitNewModifier(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Any(x =>
            x.GetSyntax() is MethodDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.NewKeyword)
        );

    private static string ParameterDeclaration(IParameterSymbol parameter)
    {
        var modifiers = new List<string>();
        if (parameter.IsThis)
            modifiers.Add("this");
        if (parameter.ScopedKind != ScopedKind.None)
            modifiers.Add("scoped");
        if (parameter.IsParams)
            modifiers.Add("params");
        modifiers.Add(
            parameter.RefKind switch
            {
                RefKind.Ref => "ref",
                RefKind.Out => "out",
                RefKind.In => "in",
                RefKind.RefReadOnlyParameter => "ref readonly",
                _ => string.Empty,
            }
        );
        var prefix = string.Join(" ", modifiers.Where(x => x.Length > 0));
        return $"{(prefix.Length == 0 ? string.Empty : prefix + " ")}{TypeName(parameter.Type)} {Escape(parameter.Name)}";
    }

    private static string BuildTypeDeclaration(INamedTypeSymbol type)
    {
        var modifiers = new List<string> { AccessibilityText(type.DeclaredAccessibility) };
        if (type.IsStatic)
            modifiers.Add("static");
        else if (type.TypeKind == TypeKind.Class)
        {
            if (type.IsAbstract)
                modifiers.Add("abstract");
            if (type.IsSealed)
                modifiers.Add("sealed");
        }
        else if (type.TypeKind == TypeKind.Struct)
        {
            if (type.IsReadOnly)
                modifiers.Add("readonly");
            if (type.IsRefLikeType)
                modifiers.Add("ref");
        }
        modifiers.Add("partial");
        modifiers.Add(
            type switch
            {
                { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
                { IsRecord: true } => "record",
                { TypeKind: TypeKind.Struct } => "struct",
                { TypeKind: TypeKind.Interface } => "interface",
                _ => "class",
            }
        );
        return $"{string.Join(" ", modifiers)} {Escape(type.Name)}{TypeParameters(type.TypeParameters)}{ConstraintClauses(type.TypeParameters)}";
    }

    private static IEnumerable<INamedTypeSymbol> GetTypeHierarchy(INamedTypeSymbol type)
    {
        var stack = new Stack<INamedTypeSymbol>();
        for (var current = type; current != null; current = current.ContainingType)
        {
            stack.Push(current);
        }
        return stack;
    }

    private static string TypeParameters(ImmutableArray<ITypeParameterSymbol> typeParameters) =>
        typeParameters.Length == 0 ? string.Empty : $"<{string.Join(", ", typeParameters.Select(x => $"{Variance(x)}{Escape(x.Name)}"))}>";

    private static string Variance(ITypeParameterSymbol typeParameter) =>
        typeParameter.Variance switch
        {
            VarianceKind.In => "in ",
            VarianceKind.Out => "out ",
            _ => string.Empty,
        };

    private static string ConstraintClauses(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        var clauses = new List<string>();
        foreach (var typeParameter in typeParameters)
        {
            var constraints = new List<string>();
            if (typeParameter.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (typeParameter.HasValueTypeConstraint)
                constraints.Add("struct");
            else if (typeParameter.HasReferenceTypeConstraint)
                constraints.Add(
                    typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class"
                );
            else if (typeParameter.HasNotNullConstraint)
                constraints.Add("notnull");

            constraints.AddRange(typeParameter.ConstraintTypes.Select(TypeName));
            if (typeParameter.HasConstructorConstraint)
                constraints.Add("new()");
            if (constraints.Count > 0)
                clauses.Add($"where {Escape(typeParameter.Name)} : {string.Join(", ", constraints)}");
        }
        return clauses.Count == 0 ? string.Empty : " " + string.Join(" ", clauses);
    }

    private static bool RequiresUnsafeModifier(IMethodSymbol method) =>
        method.ReturnType.TypeKind == TypeKind.Pointer || method.Parameters.Any(x => x.Type.TypeKind == TypeKind.Pointer);

    private MappingMethodConfiguration? RootConfiguration(MappingContext context, ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        var configuration = context.Configuration;
        return
            configuration != null
            && SymbolEqualityComparer.Default.Equals(configuration.SourceType, sourceType)
            && SymbolEqualityComparer.Default.Equals(configuration.TargetType, targetType)
            ? configuration
            : null;
    }

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

        var candidates = DiscoverMappingMethods()
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
                Diagnostic.Create(IncompleteSource, configuration.Method.Locations.FirstOrDefault(), configuration.Method.Name, member.Name)
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

    private static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated
        || type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static ITypeSymbol NonNullableType(ITypeSymbol type) =>
        type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? named.TypeArguments[0]
        : type.IsReferenceType ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
        : type;

    private static string NonNullExpression(string expression, ITypeSymbol type) =>
        type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? $"({expression}).Value"
            : $"{expression}!";

    private static string? BuildEmptyCollectionExpression(ITypeSymbol targetType)
    {
        if (targetType is IArrayTypeSymbol array)
            return $"global::System.Array.Empty<{TypeName(array.ElementType)}>()";
        if (TryGetDictionaryTypes(targetType, out var key, out var value))
        {
            var creationType = DictionaryCreationType(targetType, key, value);
            return creationType == null ? null : $"new {creationType}()";
        }
        if (TryGetSequenceElement(targetType, out var element))
            return BuildSequenceCreation(targetType, element, "0");
        return null;
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
        var mappingNames = DiscoverMappingMethods().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                        Diagnostic.Create(
                            InvalidConfiguration,
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

    private static IReadOnlyDictionary<string, ImmutableArray<IMethodSymbol>> IndexConfigurationHelpers(INamedTypeSymbol mapperType)
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

    private static AttributeData? Attribute(ISymbol symbol, string attributeName) => Attributes(symbol, attributeName).FirstOrDefault();

    private static IEnumerable<AttributeData> Attributes(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().Where(x => string.Equals(x.AttributeClass?.ToDisplayString(), attributeName, StringComparison.Ordinal));

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
        _diagnostics.Add(Diagnostic.Create(InvalidConfiguration, method.Locations.FirstOrDefault(), method.Name, reason));

    private static string? ReadFactoryName(IMethodSymbol method)
    {
        var attribute = method
            .GetAttributes()
            .FirstOrDefault(x => string.Equals(x.AttributeClass?.ToDisplayString(), MapToFactoryAttribute, StringComparison.Ordinal));
        return attribute?.ConstructorArguments is [{ Value: string value }] ? value : null;
    }

    private static int ReadDomainFactoryInput(IMethodSymbol method)
    {
        var attribute = method
            .GetAttributes()
            .First(x => string.Equals(x.AttributeClass?.ToDisplayString(), DomainFactoryAttribute, StringComparison.Ordinal));
        var input = attribute.NamedArguments.FirstOrDefault(x => string.Equals(x.Key, "Input", StringComparison.Ordinal)).Value.Value;
        return input == null ? 0 : Convert.ToInt32(input, CultureInfo.InvariantCulture);
    }

    private static bool HasAttribute(IMethodSymbol method, string attributeName) =>
        method.GetAttributes().Any(x => string.Equals(x.AttributeClass?.ToDisplayString(), attributeName, StringComparison.Ordinal));

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

    private static bool SetsRequiredMembers(IMethodSymbol constructor) =>
        constructor
            .GetAttributes()
            .Any(x =>
                string.Equals(
                    x.AttributeClass?.ToDisplayString(),
                    "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute",
                    StringComparison.Ordinal
                )
            );

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

    private static bool TryGetSequenceElement(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            elementType = null!;
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            elementType = array.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol named)
        {
            var sequence = named
                .AllInterfaces.Append(named)
                .FirstOrDefault(x => x.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
            if (sequence != null)
            {
                elementType = sequence.TypeArguments[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool TryGetDictionaryTypes(ITypeSymbol type, out ITypeSymbol keyType, out ITypeSymbol valueType)
    {
        if (type is INamedTypeSymbol named)
        {
            var dictionary = named.AllInterfaces.Append(named).FirstOrDefault(IsDictionaryType);
            if (dictionary != null)
            {
                keyType = dictionary.TypeArguments[0];
                valueType = dictionary.TypeArguments[1];
                return true;
            }
        }

        keyType = null!;
        valueType = null!;
        return false;
    }

    private static bool IsDictionaryType(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition.ToDisplayString();
        return string.Equals(definition, "System.Collections.Generic.IDictionary<TKey, TValue>", StringComparison.Ordinal)
            || string.Equals(definition, "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>", StringComparison.Ordinal)
            || string.Equals(definition, "System.Collections.Generic.Dictionary<TKey, TValue>", StringComparison.Ordinal);
    }

    private static INamedTypeSymbol? FindGenericContract(ITypeSymbol type, params string[] definitions)
    {
        if (type is not INamedTypeSymbol named)
            return null;
        return named
            .AllInterfaces.Append(named)
            .FirstOrDefault(x => definitions.Contains(x.OriginalDefinition.ToDisplayString(), StringComparer.Ordinal));
    }

    private string? CountExpression(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
            return "source.Length";
        if (HasAccessibleCount(type))
            return "source.Count";
        var contract = FindGenericContract(
            type,
            "System.Collections.Generic.ICollection<T>",
            "System.Collections.Generic.IReadOnlyCollection<T>"
        );
        return contract == null ? null : $"(({TypeName(contract)})source).Count";
    }

    private string? IndexExpression(ITypeSymbol type, string sourceExpression, string indexExpression)
    {
        if (type is IArrayTypeSymbol)
            return $"{sourceExpression}[{indexExpression}]";
        if (HasAccessibleIndexer(type))
            return $"{sourceExpression}[{indexExpression}]";
        var contract = FindGenericContract(type, "System.Collections.Generic.IList<T>", "System.Collections.Generic.IReadOnlyList<T>");
        return contract == null ? null : $"(({TypeName(contract)}){sourceExpression})[{indexExpression}]";
    }

    private static string EnumerableExpression(ITypeSymbol type, ITypeSymbol elementType, string sourceExpression) => sourceExpression;

    private string DictionaryCountExpression(ITypeSymbol type, string sourceExpression)
    {
        if (HasAccessibleCount(type))
            return $"{sourceExpression}.Count";
        var contract = FindGenericContract(
            type,
            "System.Collections.Generic.IDictionary<TKey, TValue>",
            "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>",
            "System.Collections.Generic.Dictionary<TKey, TValue>"
        );
        return contract == null ? "0" : $"(({TypeName(contract)}){sourceExpression}).Count";
    }

    private static string DictionaryExpression(ITypeSymbol type, ITypeSymbol keyType, ITypeSymbol valueType, string sourceExpression) =>
        sourceExpression;

    private bool HasAccessibleCount(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && DirectlyAccessibleTypes(named)
            .SelectMany(x => x.GetMembers("Count"))
            .OfType<IPropertySymbol>()
            .Any(x => !x.IsStatic && x.GetMethod != null && IsAccessible(x.GetMethod));

    private bool HasAccessibleIndexer(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && DirectlyAccessibleTypes(named)
            .SelectMany(x => x.GetMembers())
            .OfType<IPropertySymbol>()
            .Any(x => x.IsIndexer && !x.IsStatic && x.GetMethod != null && IsAccessible(x.GetMethod));

    private static IEnumerable<INamedTypeSymbol> DirectlyAccessibleTypes(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Interface ? type.AllInterfaces.Append(type) : [type];

    private static bool CanCreateSequenceTarget(ITypeSymbol targetType)
    {
        if (targetType is IArrayTypeSymbol)
            return true;
        if (targetType is not INamedTypeSymbol named)
            return false;
        var definition = named.OriginalDefinition.ToDisplayString();
        return string.Equals(definition, "System.Collections.Generic.List<T>", StringComparison.Ordinal)
            || string.Equals(definition, "System.Collections.Generic.IEnumerable<T>", StringComparison.Ordinal)
            || string.Equals(definition, "System.Collections.Generic.ICollection<T>", StringComparison.Ordinal)
            || string.Equals(definition, "System.Collections.Generic.IReadOnlyCollection<T>", StringComparison.Ordinal)
            || string.Equals(definition, "System.Collections.Generic.IList<T>", StringComparison.Ordinal)
            || string.Equals(definition, "System.Collections.Generic.IReadOnlyList<T>", StringComparison.Ordinal);
    }

    private static string? BuildSequenceCreation(ITypeSymbol targetType, ITypeSymbol targetElement, string? capacity)
    {
        if (!CanCreateSequenceTarget(targetType) || targetType is IArrayTypeSymbol)
            return null;
        var targetIsList =
            targetType is INamedTypeSymbol named
            && string.Equals(named.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.List<T>", StringComparison.Ordinal);
        var constructedType = targetIsList ? TypeName(targetType) : $"global::System.Collections.Generic.List<{TypeName(targetElement)}>";
        return capacity == null ? $"new {constructedType}()" : $"new {constructedType}({capacity})";
    }

    private static string? DictionaryCreationType(ITypeSymbol targetType, ITypeSymbol targetKey, ITypeSymbol targetValue)
    {
        if (targetType is not INamedTypeSymbol named)
            return null;
        var definition = named.OriginalDefinition.ToDisplayString();
        if (string.Equals(definition, "System.Collections.Generic.Dictionary<TKey, TValue>", StringComparison.Ordinal))
            return TypeName(targetType);
        if (
            string.Equals(definition, "System.Collections.Generic.IDictionary<TKey, TValue>", StringComparison.Ordinal)
            || string.Equals(definition, "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>", StringComparison.Ordinal)
        )
        {
            return $"global::System.Collections.Generic.Dictionary<{TypeName(targetKey)}, {TypeName(targetValue)}>";
        }
        return null;
    }

    private static string SequenceName(ITypeSymbol targetType, ITypeSymbol targetElement)
    {
        if (targetType is INamedTypeSymbol named && string.Equals(named.Name, "List", StringComparison.Ordinal))
            return $"ListOf{Sanitize(targetElement.Name)}";
        return $"SequenceOf{Sanitize(targetElement.Name)}";
    }

    private bool ReserveHelper(string key, string baseName, out string helperName)
    {
        if (_helperNames.TryGetValue(key, out helperName!))
            return false;

        helperName = baseName;
        var suffix = 2;
        while (!_usedHelperNames.Add(helperName))
        {
            helperName = $"{baseName}{suffix}";
            suffix++;
        }
        _helperNames.Add(key, helperName);
        return true;
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

    private static string BuildHelperKey(ITypeSymbol sourceType, ITypeSymbol targetType, MappingContext context)
    {
        var typeParameters = string.Join(",", context.MethodTypeParameters.Select(x => x.ToDisplayString(TypeDisplayFormat)));
        var constraints = ConstraintClauses(context.MethodTypeParameters);
        var ambientValues = string.Join(",", context.AmbientValues.Select(x => $"{x.Name}:{TypeName(x.Type)}"));
        var depth = context.Configuration?.MaximumDepth?.ToString(CultureInfo.InvariantCulture) ?? "none";
        var configurationIdentity =
            context.Configuration == null
                ? "convention"
                : $"{context.Configuration.Method.ToDisplayString()}@{context.Configuration.Method.Locations.FirstOrDefault()?.SourceSpan.Start ?? -1}"
                    + $"|depth-behavior:{context.Configuration.DepthExhaustionBehavior}";
        return $"{TypeName(sourceType)}->{TypeName(targetType)}|<{typeParameters}>{constraints}|{ambientValues}|depth:{depth}|{configurationIdentity}";
    }

    private static string BuildHelperDeclaration(ITypeSymbol targetType, string helperName, ITypeSymbol sourceType, MappingContext context)
    {
        var parameters = new List<string> { $"{TypeName(sourceType)} source" };
        parameters.AddRange(context.AmbientValues.Select((value, index) => $"{TypeName(value.Type)} __ambient{index}"));
        if (context.Configuration?.MaximumDepth != null)
            parameters.Add("int __depth");
        return $"private static {TypeName(targetType)} {Escape(helperName)}{TypeParameters(context.MethodTypeParameters)}({string.Join(", ", parameters)}){ConstraintClauses(context.MethodTypeParameters)}";
    }

    private static string BuildHelperCall(string helperName, string sourceExpression, MappingContext context)
    {
        var typeArguments =
            context.MethodTypeParameters.Length == 0
                ? string.Empty
                : $"<{string.Join(", ", context.MethodTypeParameters.Select(x => Escape(x.Name)))}>";
        var arguments = new[] { sourceExpression }.Concat(context.AmbientValues.Select(x => x.Expression));
        if (context.Configuration?.MaximumDepth != null)
        {
            var depth = context.IsHelper
                ? "__depth - 1"
                : (context.Configuration.MaximumDepth.Value - 1).ToString(CultureInfo.InvariantCulture);
            arguments = arguments.Append(depth);
        }
        return $"{Escape(helperName)}{typeArguments}({string.Join(", ", arguments)})";
    }

    private static string BuildHintName(INamedTypeSymbol mapperType)
    {
        var identity = mapperType.ToDisplayString(TypeDisplayFormat);
        return $"{Sanitize(identity)}_{StableHash(identity):X8}.g.cs";
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }

    private static bool TypesEqual(ITypeSymbol left, ITypeSymbol right) => SymbolEqualityComparer.IncludeNullability.Equals(left, right);

    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(TypeDisplayFormat);

    private static string AccessibilityText(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };

    private static string Escape(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : $"@{identifier}";

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
                builder.Append(character);
        }
        return builder.Length == 0 ? "Mapping" : builder.ToString();
    }

    private void ReportUnsupported(IMethodSymbol method) =>
        _diagnostics.Add(Diagnostic.Create(UnsupportedMethod, method.Locations.FirstOrDefault(), method.Name));

    private void ReportCannotConstruct(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol targetType) =>
        _diagnostics.Add(
            Diagnostic.Create(
                CannotConstruct,
                method.Locations.FirstOrDefault(),
                targetType.ToDisplayString(),
                sourceType.ToDisplayString()
            )
        );

    private sealed class DeferredObjectCreation
    {
        public DeferredObjectCreation(string creation, string assignments)
        {
            Creation = creation;
            Assignments = assignments;
        }

        public string Creation { get; }

        public string Assignments { get; }

        public string ToMarker() =>
            $"__DOMAINMAPPER_CREATE__({Convert.ToBase64String(Encoding.UTF8.GetBytes(Creation))}|{Convert.ToBase64String(Encoding.UTF8.GetBytes(Assignments))})__";
    }
}
