using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    [SuppressMessage("Maintainability", "MA0051", Justification = "Keeps root mapping mode selection and validation auditable.")]
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
        _configurations[method] = configuration;
        var ambientValues = method.Parameters.Skip(1).Select(x => new MappingValue(x.Name, x.Type, Escape(x.Name))).ToImmutableArray();
        var context = new MappingContext(method.TypeParameters, ambientValues, configuration);
        var factoryName = ReadFactoryName(method);
        IMethodSymbol? selectedFactory = null;

        if (configuration.PreserveReferences)
        {
            if (
                IsNullable(sourceParameter.Type)
                || factoryName != null
                || !CanTrackObject(sourceParameter.Type, method.ReturnType, context)
            )
            {
                _diagnostics.Add(
                    DiagnosticData.Create(
                        MapperDiagnostics.UnsupportedReferenceTracking,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        "tracking requires a non-null source and a target that can be allocated before its mapped members"
                    )
                );
                return;
            }

            var trackedExpression = QueueObjectHelper(sourceParameter.Type, method.ReturnType, sourceExpression, context);
            if (trackedExpression == null)
                return;
            if (!ValidateSourceCompleteness(configuration, null, null))
                return;
            var trackedHooks = BuildCompletionHooks(configuration, sourceExpression, "target", context);
            if (trackedHooks == null)
                return;
            var referenceKeyName = EnsureReferenceKey();
            var trackedBody =
                $"var __references = new global::System.Collections.Generic.Dictionary<{referenceKeyName}, object>();\n"
                + $"var target = {trackedExpression};\n"
                + trackedHooks
                + "return target;";
            _rootContracts.Add(new MappingContract(method.Name, BuildDeclaration(method), trackedBody, MappingShape.Create));
            _successfulMappingMethods.Add(method);
            return;
        }

        var plan =
            factoryName == null
                ? BuildRootExpression(sourceParameter.Type, method.ReturnType, sourceExpression, context)
                : CreationPlan.FromExpression(
                    BuildFactoryExpression(
                        method.ReturnType,
                        sourceParameter,
                        method.Parameters.Skip(1),
                        factoryName,
                        method,
                        context,
                        out selectedFactory
                    )
                );

        if (plan == null)
        {
            if (factoryName == null)
            {
                // Surface which source members could not be consumed alongside the construction failure.
                ValidateSourceCompleteness(configuration, null, null);
                ReportCannotConstruct(method, sourceParameter.Type, method.ReturnType);
            }
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
        var body = $"{plan.ToTargetStatements()}\n{guardedHooks}return target;";
        _rootContracts.Add(new MappingContract(method.Name, BuildDeclaration(method), body, MappingShape.Create));
        _successfulMappingMethods.Add(method);
    }

    private CreationPlan? BuildRootExpression(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        MappingContext context
    )
    {
        if (TypesEqual(sourceType, targetType))
            return new CreationPlan(sourceExpression);

        if (sourceType.IsReferenceType && sourceType.NullableAnnotation == NullableAnnotation.Annotated)
            return CreationPlan.FromExpression(ConvertExpression(sourceType, targetType, sourceExpression, context));

        if (TryGetDictionaryTypes(sourceType, out _, out _) && TryGetDictionaryTypes(targetType, out _, out _))
            return CreationPlan.FromExpression(ConvertExpression(sourceType, targetType, sourceExpression, context));

        if (TryGetSequenceElement(sourceType, out _) && TryGetSequenceElement(targetType, out _))
            return CreationPlan.FromExpression(ConvertExpression(sourceType, targetType, sourceExpression, context));

        var objectCreation = BuildObjectCreation(sourceType, targetType, sourceExpression, context);
        if (objectCreation != null || HasTargetConfiguration(RootConfiguration(context, sourceType, targetType)))
            return objectCreation;
        return CreationPlan.FromExpression(ConvertExpression(sourceType, targetType, sourceExpression, context));
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
        MappingContext context,
        out bool valid
    )
    {
        valid = true;
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
        {
            valid = false;
            ReportInvalidConfiguration(
                configuration!.Method,
                $"condition method '{condition!.Name}' has an unsupported parameter contract"
            );
        }
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
        _configurations[method] = configuration;
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
        if (request.Context.Configuration?.PreserveReferences == true)
        {
            BuildTrackedObjectHelper(request);
            return;
        }

        var plan = BuildObjectCreation(request.SourceType, request.TargetType, "source", request.Context);
        if (plan == null)
        {
            if (request.Context.Configuration != null)
                _successfulMappingMethods.Remove(request.Context.Configuration.Method);
            _diagnostics.Add(
                DiagnosticData.Create(
                    MapperDiagnostics.CannotConstruct,
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
                $"{depthGuard}{plan.ToTargetStatements()}\nreturn target;",
                MappingShape.Helper
            )
        );
    }

    private void BuildTrackedObjectHelper(MappingRequest request)
    {
        if (!CanTrackObject(request.SourceType, request.TargetType, request.Context))
        {
            _successfulMappingMethods.Remove(request.Context.Configuration!.Method);
            _diagnostics.Add(
                DiagnosticData.Create(
                    MapperDiagnostics.UnsupportedReferenceTracking,
                    _mapperType.Locations.FirstOrDefault(),
                    request.Context.Configuration!.Method.Name,
                    $"target '{request.TargetType.ToDisplayString()}' cannot be allocated before its mapped members"
                )
            );
            AddUnsupportedTrackedHelper(request);
            return;
        }

        if (
            !TryBuildAssignments(
                request.SourceType,
                request.TargetType,
                "source",
                "target",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                false,
                request.Context,
                out var assignments
            ) || (assignments.Length == 0 && HasReadableState(request.TargetType))
        )
        {
            _successfulMappingMethods.Remove(request.Context.Configuration!.Method);
            _diagnostics.Add(
                DiagnosticData.Create(
                    MapperDiagnostics.UnsupportedReferenceTracking,
                    _mapperType.Locations.FirstOrDefault(),
                    request.Context.Configuration!.Method.Name,
                    $"target '{request.TargetType.ToDisplayString()}' cannot be fully assigned after its tracked instance is allocated"
                )
            );
            AddUnsupportedTrackedHelper(request);
            return;
        }

        var declaration = BuildHelperDeclaration(request.TargetType, request.MethodName, request.SourceType, request.Context);
        var depthGuard = BuildDepthGuard(request.TargetType, request.Context);
        var referenceKeyName = EnsureReferenceKey();
        var body =
            $"var __referenceKey = new {referenceKeyName}(source, typeof({RuntimeTypeName(request.TargetType)}));\n"
            + $"if (__references.TryGetValue(__referenceKey, out var __existing))\n{{\n    return ({TypeName(request.TargetType)})__existing;\n}}\n"
            + depthGuard
            + $"var target = new {TypeName(request.TargetType)}();\n"
            + "__references.Add(__referenceKey, target);\n"
            + assignments
            + (assignments.Length == 0 ? string.Empty : "\n")
            + "return target;";
        _helperContracts.Add(new MappingContract(request.MethodName, declaration, body, MappingShape.Helper));
    }

    private void AddUnsupportedTrackedHelper(MappingRequest request) =>
        _helperContracts.Add(
            new MappingContract(
                request.MethodName,
                BuildHelperDeclaration(request.TargetType, request.MethodName, request.SourceType, request.Context),
                "throw new global::System.InvalidOperationException(\"Unsupported DomainMapper reference-tracking contract.\");",
                MappingShape.Helper
            )
        );

    private bool CanTrackObject(ITypeSymbol sourceType, ITypeSymbol targetType, MappingContext context)
    {
        if (
            !sourceType.IsReferenceType
            || targetType is not INamedTypeSymbol namedTarget
            || !targetType.IsReferenceType
            || namedTarget.IsAbstract
        )
            return false;
        if (!namedTarget.InstanceConstructors.Any(x => x.Parameters.Length == 0 && IsAccessible(x)))
            return false;
        if (RequiredFields(targetType).Count > 0 || SettableTargetMembers(targetType, context.Configuration).Any(x => x.IsInitOnly))
            return false;
        return true;
    }

    private static string BuildDepthGuard(ITypeSymbol targetType, MappingContext context)
    {
        if (context.Configuration?.MaximumDepth == null)
            return string.Empty;
        return context.Configuration.DepthExhaustionBehavior == 1
            ? "if (__depth <= 0)\n{\n    throw new global::System.InvalidOperationException(\"DomainMapper maximum mapping depth was exhausted.\");\n}\n"
            : $"if (__depth <= 0)\n{{\n    return default({TypeName(targetType)})!;\n}}\n";
    }

    private CreationPlan? BuildObjectCreation(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        MappingContext context
    )
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

            return new CreationPlan($"new {TypeName(targetType)}(){initializer}", assignments);
        }

        return null;
    }

    private CreationPlan? BuildConstructorCreation(
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

        return new CreationPlan($"new {TypeName(targetType)}({string.Join(", ", arguments)}){initializer}", assignments);
    }

    [SuppressMessage(
        "Maintainability",
        "MA0051",
        Justification = "Keeps member assignment and explicit mutation planning in one ordered flow."
    )]
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
                && CollectionPolicy(configuration, targetMember.Name) is not (1 or 2)
            )
            {
                assignments = string.Empty;
                return false;
            }
        }

        var lines = new List<string>();
        var assignmentMembers = writableMembers
            .Concat(ReadableTargetMembers(targetType, configuration).Where(x => CollectionPolicy(configuration, x.Name) is 1 or 2))
            .GroupBy(x => x.Symbol, SymbolEqualityComparer.Default)
            .Select(x => x.First());
        foreach (var targetMember in assignmentMembers)
        {
            if (consumedMembers.Contains(targetMember.Name))
                continue;
            if (configuration?.IgnoredTargets.Contains(targetMember.Name) == true)
                continue;
            if (configuration?.OnlyTargets != null && !configuration.OnlyTargets.Contains(targetMember.Name))
                continue;

            if (CollectionPolicy(configuration, targetMember.Name) is 1 or 2)
            {
                if (
                    !TryBuildCollectionMutation(
                        sourceType,
                        targetType,
                        sourceExpression,
                        targetExpression,
                        targetMember,
                        context,
                        out var mutation
                    )
                )
                {
                    assignments = string.Empty;
                    return false;
                }
                lines.Add(mutation);
                continue;
            }

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
                context,
                out var conditionValid
            );
            if (!conditionValid)
            {
                assignments = string.Empty;
                return false;
            }
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

    [SuppressMessage("Maintainability", "MA0051", Justification = "Keeps collection null, shape, and mutation policy validation together.")]
    private bool TryBuildCollectionMutation(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        string targetExpression,
        MappingMember targetMember,
        MappingContext context,
        out string mutation
    )
    {
        mutation = string.Empty;
        var configuration = context.Configuration!;
        string sourceValue;
        ITypeSymbol sourceValueType;
        if (configuration.Bindings.TryGetValue(targetMember.Name, out var binding))
        {
            sourceValue = BuildSourcePathExpression(sourceExpression, binding.SourceMembers);
            sourceValueType = EffectivePathType(binding.SourceMembers);
        }
        else if (TryFindMember(ReadableMembers(sourceType), targetMember.Name, out var sourceMember))
        {
            sourceValue = $"{sourceExpression}.{Escape(sourceMember.Name)}";
            sourceValueType = sourceMember.Type;
        }
        else
        {
            ReportInvalidConfiguration(
                configuration.Method,
                $"collection policy target '{targetMember.Name}' has no configured source collection"
            );
            return false;
        }

        var nonNullableSource = NonNullableType(sourceValueType);
        var targetAccess = $"{targetExpression}.{Escape(targetMember.Name)}";
        var collectionVariable = "__collection_" + Sanitize(targetMember.Name);
        var lines = new List<string>();
        var policy = configuration.CollectionPolicies[targetMember.Name];
        var behavior = configuration.NullBehaviors.TryGetValue(targetMember.Name, out var configuredBehavior) ? configuredBehavior : 0;
        var nullable = IsNullable(sourceValueType);
        var sourceCollectionVariable = "__sourceCollection_" + Sanitize(targetMember.Name);
        var collectionSource = nullable ? sourceCollectionVariable : sourceValue;

        if (
            TryGetDictionaryTypes(nonNullableSource, out var sourceKey, out var sourceValueTypeArgument)
            && TryGetDictionaryTypes(targetMember.Type, out var targetKey, out var targetValue)
        )
        {
            var contract = FindGenericContract(targetMember.Type, "System.Collections.Generic.IDictionary<TKey, TValue>");
            if (contract == null)
                return false;
            var helperContext = context.ForHelper();
            var key = ConvertExpression(sourceKey, targetKey, "item.Key", helperContext);
            var value = ConvertExpression(sourceValueTypeArgument, targetValue, "item.Value", helperContext);
            if (key == null || value == null)
                return false;
            lines.Add(
                $"var {collectionVariable} = ({TypeName(contract)})({targetAccess} ?? throw new global::System.InvalidOperationException(\"Target collection '{Escape(targetMember.Name)}' cannot be null.\"));"
            );
            if (policy == 1)
                lines.Add($"{collectionVariable}.Clear();");
            lines.Add($"foreach (var item in {collectionSource})\n{{\n    {collectionVariable}.Add({key}, {value});\n}}");
        }
        else if (
            TryGetSequenceElement(nonNullableSource, out var sourceElement)
            && TryGetSequenceElement(targetMember.Type, out var targetElement)
        )
        {
            var contract = FindGenericContract(targetMember.Type, "System.Collections.Generic.ICollection<T>");
            if (contract == null)
                return false;
            var value = ConvertExpression(sourceElement, targetElement, "item", context.ForHelper());
            if (value == null)
                return false;
            lines.Add(
                $"var {collectionVariable} = ({TypeName(contract)})({targetAccess} ?? throw new global::System.InvalidOperationException(\"Target collection '{Escape(targetMember.Name)}' cannot be null.\"));"
            );
            if (policy == 1)
                lines.Add($"{collectionVariable}.Clear();");
            lines.Add($"foreach (var item in {collectionSource})\n{{\n    {collectionVariable}.Add({value});\n}}");
        }
        else
        {
            ReportInvalidConfiguration(
                configuration.Method,
                $"collection policy target '{targetMember.Name}' has incompatible source and target collection shapes"
            );
            return false;
        }

        var body = string.Join("\n", lines);
        if (nullable)
        {
            if (behavior == 2)
            {
                body =
                    $"if ({collectionSource} is null)\n{{\n    throw new global::System.InvalidOperationException(\"Source collection for '{Escape(targetMember.Name)}' cannot be null.\");\n}}\n{body}";
            }
            else if (behavior == 1 || policy != 1)
            {
                body = $"if ({collectionSource} is not null)\n{{\n{Indent(body)}\n}}";
            }
            else
            {
                var collectionContract = FindGenericContract(
                    targetMember.Type,
                    "System.Collections.Generic.ICollection<T>",
                    "System.Collections.Generic.IDictionary<TKey, TValue>"
                )!;
                var clear =
                    $"(({TypeName(collectionContract)})({targetAccess} ?? throw new global::System.InvalidOperationException(\"Target collection '{Escape(targetMember.Name)}' cannot be null.\"))).Clear();";
                body = $"if ({collectionSource} is null)\n{{\n    {clear}\n}}\nelse\n{{\n{Indent(string.Join("\n", lines))}\n}}";
            }
            body = $"var {sourceCollectionVariable} = {sourceValue};\n{body}";
        }

        var condition = BuildConditionExpression(
            sourceType,
            targetType,
            sourceExpression,
            targetExpression,
            targetMember.Name,
            context,
            out var conditionValid
        );
        if (!conditionValid)
            return false;
        mutation = condition == null ? body : $"if ({condition})\n{{\n{Indent(body)}\n}}";
        return true;
    }

    private static int? CollectionPolicy(MappingMethodConfiguration? configuration, string targetMemberName) =>
        configuration?.CollectionPolicies.TryGetValue(targetMemberName, out var policy) == true ? policy : null;

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

            var condition = BuildConditionExpression(
                sourceType,
                targetType,
                sourceExpression,
                "target",
                targetMember.Name,
                context,
                out var conditionValid
            );
            if (!conditionValid)
            {
                initializer = string.Empty;
                assignments = string.Empty;
                return false;
            }
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

        if (consumedMembers.Count == 0 && initializerEntries.Count == 0 && assignmentLines.Count == 0 && HasReadableState(targetType))
        {
            initializer = string.Empty;
            assignments = string.Empty;
            return false;
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
            || configuration.CollectionPolicies.Count > 0
            || configuration.ComputedMembers.Count > 0
            || configuration.Conditions.Count > 0
            || configuration.PreserveReferences
            || !configuration.EnforceTarget
        );

    private bool CanConstructObject(ITypeSymbol sourceType, ITypeSymbol targetType, MappingContext context, ISet<string> visiting)
    {
        var key = BuildHelperKey(sourceType, targetType, context);
        if (!visiting.Add(key))
            return context.Configuration?.MaximumDepth != null || context.Configuration?.PreserveReferences == true;

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
                var consumesSource =
                    consumed.Count > 0
                    || settableMembers.Any(x => !consumed.Contains(x.Name) || (x.IsRequired && !constructorSetsRequiredMembers));
                if (assignmentsValid && inaccessibleStateIsSafe && (consumesSource || !HasReadableState(targetType)))
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
}
