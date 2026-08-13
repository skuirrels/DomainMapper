using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMapper.Engine;

internal sealed class MapperCompiler
{
    private const string DomainFactoryAttribute = "DomainMapper.Abstractions.DomainFactoryAttribute";
    private const string MapToFactoryAttribute = "DomainMapper.Abstractions.MapToFactoryAttribute";

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

    private MapperCompiler(INamedTypeSymbol mapperType, Compilation compilation)
    {
        _mapperType = mapperType;
        _compilation = compilation;
        foreach (var memberName in GetTypeHierarchy(mapperType).SelectMany(x => x.GetMembers()).Select(x => x.Name))
        {
            _usedHelperNames.Add(memberName);
        }
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

    private IEnumerable<IMethodSymbol> DiscoverMappingMethods() =>
        _mapperType
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(x => x.IsPartialDefinition && x.PartialImplementationPart == null)
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue);

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
        var context = new MappingContext(method.TypeParameters, ImmutableArray<MappingValue>.Empty);
        var factoryName = ReadFactoryName(method);
        if (factoryName == null && method.Parameters.Length > 1)
        {
            ReportUnsupported(method);
            return;
        }

        var expression =
            factoryName == null
                ? BuildRootExpression(sourceParameter.Type, method.ReturnType, sourceExpression, context)
                : BuildFactoryExpression(method.ReturnType, sourceParameter, method.Parameters.Skip(1), factoryName, method, context);

        if (expression == null)
        {
            if (factoryName == null)
                ReportCannotConstruct(method, sourceParameter.Type, method.ReturnType);
            return;
        }

        var body = $"var target = {expression};\nreturn target;";
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

        return BuildObjectCreation(sourceType, targetType, sourceExpression, context)
            ?? ConvertExpression(sourceType, targetType, sourceExpression, context);
    }

    private void BuildUpdateContract(IMethodSymbol method)
    {
        var source = method.Parameters[0];
        var target = method.Parameters[1];
        if (target.RefKind is not (RefKind.None or RefKind.Ref) || (target.Type.IsValueType && target.RefKind != RefKind.Ref))
        {
            ReportUnsupported(method);
            return;
        }

        var context = new MappingContext(method.TypeParameters, ImmutableArray<MappingValue>.Empty);
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

        _rootContracts.Add(new MappingContract(method.Name, BuildDeclaration(method), assignments, MappingShape.Update));
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
        _helperContracts.Add(
            new MappingContract(request.MethodName, declaration, $"var target = {expression};\nreturn target;", MappingShape.Helper)
        );
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
        if (targetType is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } namedTarget || namedTarget.IsAbstract)
            return null;

        var sourceMembers = ReadableProperties(sourceType);
        var constructors = namedTarget
            .InstanceConstructors.Where(IsAccessible)
            .Where(x => !IsRecordCopyConstructor(x, namedTarget))
            .OrderByDescending(x => x.Parameters.Length)
            .ToArray();

        foreach (var constructor in constructors.Where(x => x.Parameters.Length > 0))
        {
            var creation = BuildConstructorCreation(sourceType, targetType, sourceExpression, sourceMembers, constructor, context);
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
        IReadOnlyList<IPropertySymbol> sourceMembers,
        IMethodSymbol constructor,
        MappingContext context
    )
    {
        var arguments = new List<string>();
        var consumedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in constructor.Parameters)
        {
            if (!TryFindProperty(sourceMembers, parameter.Name, out var sourceMember))
                return null;

            var argument = ConvertExpression(sourceMember.Type, parameter.Type, $"{sourceExpression}.{Escape(sourceMember.Name)}", context);
            if (argument == null)
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
        var sourceMembers = ReadableProperties(sourceType);
        var writableMembers = WritableProperties(targetType);
        foreach (var targetMember in ReadableProperties(targetType))
        {
            if (
                !consumedMembers.Contains(targetMember.Name)
                && TryFindProperty(sourceMembers, targetMember.Name, out _)
                && !writableMembers.Any(x => SymbolEqualityComparer.Default.Equals(x, targetMember))
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

            if (!TryFindProperty(sourceMembers, targetMember.Name, out var sourceMember))
            {
                assignments = string.Empty;
                return false;
            }

            var value = ConvertExpression(sourceMember.Type, targetMember.Type, $"{sourceExpression}.{Escape(sourceMember.Name)}", context);
            if (value == null)
            {
                assignments = string.Empty;
                return false;
            }

            lines.Add($"{targetExpression}.{Escape(targetMember.Name)} = {value};");
        }

        assignments = string.Join("\n", lines);
        return !requireAssignment || lines.Count > 0;
    }

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
        var sourceMembers = ReadableProperties(sourceType);
        var settableMembers = SettableProperties(targetType);
        var constructorSetsRequiredMembers = SetsRequiredMembers(constructor);
        if (!constructorSetsRequiredMembers && RequiredFields(targetType).Count > 0)
        {
            initializer = string.Empty;
            assignments = string.Empty;
            return false;
        }

        foreach (var targetMember in ReadableProperties(targetType))
        {
            var requiresInitializer = targetMember.IsRequired && !constructorSetsRequiredMembers;
            if (
                (!consumedMembers.Contains(targetMember.Name) || requiresInitializer)
                && TryFindProperty(sourceMembers, targetMember.Name, out _)
                && !settableMembers.Any(x => SymbolEqualityComparer.Default.Equals(x, targetMember))
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
            var requiresInitializer = targetMember.SetMethod!.IsInitOnly || (targetMember.IsRequired && !constructorSetsRequiredMembers);
            if (consumedMembers.Contains(targetMember.Name) && !requiresInitializer)
                continue;

            if (!TryFindProperty(sourceMembers, targetMember.Name, out var sourceMember))
            {
                initializer = string.Empty;
                assignments = string.Empty;
                return false;
            }

            var value = ConvertExpression(sourceMember.Type, targetMember.Type, $"{sourceExpression}.{Escape(sourceMember.Name)}", context);
            if (value == null)
            {
                initializer = string.Empty;
                assignments = string.Empty;
                return false;
            }

            if (requiresInitializer)
                initializerEntries.Add($"{Escape(targetMember.Name)} = {value}");
            else
                assignmentLines.Add($"target.{Escape(targetMember.Name)} = {value};");
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
        MappingContext context
    )
    {
        if (targetType is not INamedTypeSymbol namedTarget)
        {
            ReportCannotConstruct(mappingMethod, sourceParameter.Type, targetType);
            return null;
        }

        var explicitValues = additionalParameters.Select(x => new MappingValue(x.Name, x.Type, Escape(x.Name))).ToArray();
        var availableValues = explicitValues
            .Concat(
                ReadableProperties(sourceParameter.Type)
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
                if (!TryFindValue(availableValues, parameter.Name, out var availableValue))
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
                return $"{TypeName(targetType)}.{Escape(factory.Name)}({string.Join(", ", arguments)})";
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
        if (!CanConstructObject(sourceType, targetType, context, new HashSet<string>(StringComparer.Ordinal)))
            return null;

        var key = BuildHelperKey(sourceType, targetType, context);
        if (ReserveHelper(key, $"MapTo{Sanitize(targetType.Name)}", out var helperName))
            _pendingHelpers.Enqueue(new MappingRequest(sourceType, targetType, helperName, context.ForHelper()));
        return BuildHelperCall(helperName, sourceExpression, context);
    }

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

                var sourceValues = ReadableProperties(sourceType)
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
            return true;

        try
        {
            if (targetType is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } namedTarget || namedTarget.IsAbstract)
                return false;

            var sourceMembers = ReadableProperties(sourceType);
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
                        !TryFindProperty(sourceMembers, parameter.Name, out var sourceMember)
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

                var settableMembers = SettableProperties(targetType);
                var assignmentsValid = settableMembers
                    .Where(x => !consumed.Contains(x.Name) || x.SetMethod!.IsInitOnly || (x.IsRequired && !constructorSetsRequiredMembers))
                    .All(x =>
                        TryFindProperty(sourceMembers, x.Name, out var sourceMember)
                        && CanConvert(sourceMember.Type, x.Type, context, visiting)
                    );
                var inaccessibleStateIsSafe = ReadableProperties(targetType)
                    .All(x =>
                        (consumed.Contains(x.Name) && (!x.IsRequired || constructorSetsRequiredMembers))
                        || !TryFindProperty(sourceMembers, x.Name, out _)
                        || settableMembers.Any(y => SymbolEqualityComparer.Default.Equals(x, y))
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

            var sourceValues = ReadableProperties(sourceType).Select(x => new MappingValue(x.Name, x.Type, string.Empty)).ToArray();
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

        var creation = payload.Substring(0, separator);
        var assignments = payload.Substring(separator + 1).Replace("\\n", "\n", StringComparison.Ordinal);
        return $"var target = {creation};\n{assignments}\nreturn target;";
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

    private IReadOnlyList<IPropertySymbol> ReadableProperties(ITypeSymbol type) =>
        GetAllProperties(type).Where(x => !x.IsStatic && !x.IsIndexer && x.GetMethod != null && IsAccessible(x.GetMethod)).ToArray();

    private IReadOnlyList<IPropertySymbol> WritableProperties(ITypeSymbol type) =>
        GetAllProperties(type)
            .Where(x => !x.IsStatic && !x.IsIndexer && x.SetMethod != null && !x.SetMethod.IsInitOnly && IsAccessible(x.SetMethod))
            .ToArray();

    private IReadOnlyList<IPropertySymbol> SettableProperties(ITypeSymbol type) =>
        GetAllProperties(type).Where(x => !x.IsStatic && !x.IsIndexer && x.SetMethod != null && IsAccessible(x.SetMethod)).ToArray();

    private static IEnumerable<IPropertySymbol> GetAllProperties(ITypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (type is not INamedTypeSymbol named)
            return [];

        var properties = new List<IPropertySymbol>();
        for (var current = named; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (seen.Add(property.Name))
                    properties.Add(property);
            }
        }

        if (named.TypeKind != TypeKind.Interface)
            return properties;

        foreach (var interfaceType in named.AllInterfaces)
        {
            foreach (var property in interfaceType.GetMembers().OfType<IPropertySymbol>())
            {
                if (seen.Add(property.Name))
                    properties.Add(property);
            }
        }

        return properties;
    }

    private static bool TryFindProperty(IReadOnlyList<IPropertySymbol> properties, string name, out IPropertySymbol property)
    {
        var exact = properties.Where(x => string.Equals(x.Name, name, StringComparison.Ordinal)).ToArray();
        if (exact.Length == 1)
        {
            property = exact[0];
            return true;
        }

        var insensitive = properties.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (insensitive.Length == 1)
        {
            property = insensitive[0];
            return true;
        }

        property = null!;
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

        return SettableProperties(targetType)
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
        return $"{TypeName(sourceType)}->{TypeName(targetType)}|<{typeParameters}>{constraints}|{ambientValues}";
    }

    private static string BuildHelperDeclaration(ITypeSymbol targetType, string helperName, ITypeSymbol sourceType, MappingContext context)
    {
        var parameters = new List<string> { $"{TypeName(sourceType)} source" };
        parameters.AddRange(context.AmbientValues.Select((value, index) => $"{TypeName(value.Type)} __ambient{index}"));
        return $"private static {TypeName(targetType)} {Escape(helperName)}{TypeParameters(context.MethodTypeParameters)}({string.Join(", ", parameters)}){ConstraintClauses(context.MethodTypeParameters)}";
    }

    private static string BuildHelperCall(string helperName, string sourceExpression, MappingContext context)
    {
        var typeArguments =
            context.MethodTypeParameters.Length == 0
                ? string.Empty
                : $"<{string.Join(", ", context.MethodTypeParameters.Select(x => Escape(x.Name)))}>";
        var arguments = new[] { sourceExpression }.Concat(context.AmbientValues.Select(x => x.Expression));
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

        public string ToMarker() => $"__DOMAINMAPPER_CREATE__({Creation}|{Assignments.Replace("\n", "\\n", StringComparison.Ordinal)})__";
    }
}
