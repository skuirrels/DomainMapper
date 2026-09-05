using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    private string? ConvertExpression(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression, MappingContext context)
    {
        if (TypesEqual(sourceType, targetType))
            return sourceExpression;

        var domainConversion = BuildDomainConversionExpression(sourceType, targetType, sourceExpression, context);
        if (domainConversion != null)
            return domainConversion;

        var declaredMapping = ResolveDeclaredMapping(sourceType, targetType, context, true, out var ambiguousMapping);
        if (ambiguousMapping)
            return null;
        if (declaredMapping != null)
            return $"{Escape(declaredMapping.Name)}({sourceExpression})";

        if (TryLiftNullableConversion(sourceType, targetType, sourceExpression, context, out var liftedExpression))
            return liftedExpression;

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

        if (sourceType.TypeKind == TypeKind.Enum && targetType.TypeKind == TypeKind.Enum)
            return BuildEnumConversion(sourceType, targetType, sourceExpression, context);

        if (targetType is INamedTypeSymbol namedTarget)
        {
            var singleValueConstructor = FindSingleValueConstructor(sourceType, namedTarget);
            if (singleValueConstructor != null)
            {
                var consumedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { singleValueConstructor.Parameters[0].Name };
                if (CanUseScalarConstructor(namedTarget, singleValueConstructor, consumedMembers))
                {
                    ReportFactoryBypass(targetType, context);
                    return $"new {TypeName(targetType)}({sourceExpression})";
                }
            }
        }

        return QueueObjectHelper(sourceType, targetType, sourceExpression, context);
    }

    /// <summary>
    /// Handles nullable targets of either kind. A nullable source is guarded and converted through its underlying type;
    /// a non-nullable source converts to the underlying target and lifts implicitly. Returns false when the target is not
    /// nullable, so the caller continues with the remaining conversion policy.
    /// </summary>
    private bool TryLiftNullableConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string sourceExpression,
        MappingContext context,
        out string? expression
    )
    {
        expression = null;
        if (targetType.IsReferenceType && targetType.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var nonNullableTarget = targetType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            if (sourceType.IsReferenceType && sourceType.NullableAnnotation == NullableAnnotation.Annotated)
            {
                var nonNullableSource = sourceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                var converted = ConvertExpression(nonNullableSource, nonNullableTarget, sourceExpression, context);
                expression = converted == null ? null : $"{sourceExpression} is null ? null : {converted}";
            }
            else if (IsNullableValueType(sourceType, out var sourceUnderlying))
            {
                var converted = ConvertExpression(
                    sourceUnderlying,
                    nonNullableTarget,
                    NonNullExpression(sourceExpression, sourceType),
                    context
                );
                expression = converted == null ? null : $"{sourceExpression} is null ? null : {converted}";
            }
            else
                expression = ConvertExpression(sourceType, nonNullableTarget, sourceExpression, context);
            return true;
        }

        if (!IsNullableValueType(targetType, out var targetUnderlying))
            return false;

        var liftedConversion = _compilation.ClassifyConversion(sourceType, targetType);
        if (liftedConversion.Exists && liftedConversion.IsImplicit)
            expression = sourceExpression;
        else if (IsNullable(sourceType))
        {
            var converted = ConvertExpression(
                NonNullableType(sourceType),
                targetUnderlying,
                NonNullExpression(sourceExpression, sourceType),
                context
            );
            expression = converted == null ? null : $"{sourceExpression} is null ? default({TypeName(targetType)}) : {converted}";
        }
        else
            expression = ConvertExpression(sourceType, targetUnderlying, sourceExpression, context);
        return true;
    }

    [SuppressMessage(
        "Maintainability",
        "MA0051",
        Justification = "Keeps sequence target allocation and reference registration ordering together."
    )]
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
        if (targetType is IArrayTypeSymbol && count == null && context.Configuration?.PreserveReferences == true)
            return null;

        var key = BuildHelperKey(sourceType, targetType, context);
        var isNew = ReserveHelper(key, $"MapTo{SequenceName(targetType, targetElement)}", out var helperName);
        if (isNew)
        {
            var referenceKeyName = helperContext.Configuration?.PreserveReferences == true ? EnsureReferenceKey() : null;
            var trackLookup =
                helperContext.Configuration?.PreserveReferences == true
                    ? $"var __referenceKey = new {referenceKeyName}(source, typeof({RuntimeTypeName(targetType)}));\nif (__references.TryGetValue(__referenceKey, out var __existing))\n{{\n    return ({TypeName(targetType)})__existing;\n}}\n{BuildDepthGuard(targetType, helperContext)}"
                    : string.Empty;
            var trackTarget =
                helperContext.Configuration?.PreserveReferences == true ? "__references.Add(__referenceKey, target);\n" : string.Empty;
            if (targetType is IArrayTypeSymbol)
            {
                string body;
                if (count == null)
                {
                    body =
                        $"var target = new global::System.Collections.Generic.List<{TypeName(targetElement)}>();\n"
                        + $"foreach (var item in source)\n{{\n    target.Add({elementExpression});\n}}\n"
                        + "return target.ToArray();";
                }
                else if (IndexExpression(sourceType, "source", "i") is { } indexedItem)
                {
                    body =
                        trackLookup
                        + $"var target = new {TypeName(targetElement)}[{count}];\n"
                        + trackTarget
                        + $"for (var i = 0; i < {count}; i++)\n{{\n    var item = {indexedItem};\n    target[i] = {elementExpression};\n}}\n"
                        + "return target;";
                }
                else
                {
                    body =
                        trackLookup
                        + $"var target = new {TypeName(targetElement)}[{count}];\n"
                        + trackTarget
                        + $"var index = 0;\nforeach (var item in source)\n{{\n    target[index++] = {elementExpression};\n}}\n"
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
                : $"foreach (var item in source)\n{{\n    target.Add({elementExpression});\n}}";
            var declaration = BuildHelperDeclaration(targetType, helperName, sourceType, helperContext);
            _helperContracts.Add(
                new MappingContract(
                    helperName,
                    declaration,
                    $"{trackLookup}var target = {creation};\n{trackTarget}{iteration}\nreturn target;",
                    MappingShape.Helper
                )
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
            var referenceKeyName = helperContext.Configuration?.PreserveReferences == true ? EnsureReferenceKey() : null;
            var trackLookup =
                helperContext.Configuration?.PreserveReferences == true
                    ? $"var __referenceKey = new {referenceKeyName}(source, typeof({RuntimeTypeName(targetType)}));\nif (__references.TryGetValue(__referenceKey, out var __existing))\n{{\n    return ({TypeName(targetType)})__existing;\n}}\n{BuildDepthGuard(targetType, helperContext)}"
                    : string.Empty;
            var trackTarget =
                helperContext.Configuration?.PreserveReferences == true ? "__references.Add(__referenceKey, target);\n" : string.Empty;
            var body =
                trackLookup
                + $"var target = new {creationType}({DictionaryCountExpression(sourceType, "source")});\n"
                + trackTarget
                + $"foreach (var item in source)\n{{\n    target[{keyExpression}] = {valueExpression};\n}}\nreturn target;";
            _helperContracts.Add(new MappingContract(helperName, declaration, body, MappingShape.Helper));
        }

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

    /// <summary>
    /// Warns once per mapping and target when generated code constructs a type directly although the type declares an
    /// accessible static factory. The mapping still generates; the warning makes the bypass visible in review.
    /// </summary>
    private void ReportFactoryBypass(ITypeSymbol targetType, MappingContext context)
    {
        if (targetType is not INamedTypeSymbol namedTarget)
            return;
        var factories = TargetFactoryMethods(namedTarget).Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        if (factories.Length == 0)
            return;

        ISymbol reportedOn = (ISymbol?)context.Configuration?.Method ?? _mapperType;
        if (!_reportedFactoryBypass.Add($"{reportedOn.ToDisplayString()}|{TypeName(targetType)}"))
            return;
        if (context.Configuration != null && IsFactoryIgnored(context.Configuration.Method, targetType))
            return;
        _diagnostics.Add(
            DiagnosticData.Create(
                MapperDiagnostics.FactoryBypassed,
                reportedOn.Locations.FirstOrDefault(),
                reportedOn.Name,
                targetType.ToDisplayString(),
                string.Join(", ", factories.Select(x => $"'{x}'"))
            )
        );
    }

    private bool IsFactoryIgnored(IMethodSymbol method, ITypeSymbol targetType)
    {
        var ignored = false;
        foreach (var attribute in Attributes(method, IgnoreTargetFactoryAttribute))
        {
            if (attribute.ConstructorArguments is [{ Value: ITypeSymbol ignoredType }] && RuntimeTypesEqual(ignoredType, targetType))
            {
                _consumedFactoryIgnores.Add(attribute);
                ignored = true;
            }
        }
        return ignored;
    }

    /// <summary>A factory ignore that no generated construction consumed is stale configuration, like any other unused contract.</summary>
    private void ValidateFactoryIgnores()
    {
        foreach (var method in _mappingMethods)
        {
            var declaration = BuildDeclaration(method);
            if (!_rootContracts.Any(x => string.Equals(x.Declaration, declaration, StringComparison.Ordinal)))
                continue;
            foreach (var attribute in Attributes(method, IgnoreTargetFactoryAttribute).Where(x => !_consumedFactoryIgnores.Contains(x)))
            {
                var ignoredType = attribute.ConstructorArguments is [{ Value: ITypeSymbol type }] ? type.ToDisplayString() : "<unknown>";
                ReportInvalidConfiguration(
                    method,
                    $"[IgnoreTargetFactory] for '{ignoredType}' is stale because the mapping does not construct that type through a constructor"
                );
            }
        }
    }

    /// <summary>
    /// Accessible static methods that return the target type from at least one non-target argument. Operators, generic
    /// methods, parameterless methods, combinators taking the target type, and static properties are not factories.
    /// </summary>
    private IEnumerable<IMethodSymbol> TargetFactoryMethods(INamedTypeSymbol targetType)
    {
        for (var current = targetType; current != null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (
                    method.IsStatic
                    && method.MethodKind == MethodKind.Ordinary
                    && !method.IsImplicitlyDeclared
                    && method.TypeParameters.Length == 0
                    && method.Parameters.Length > 0
                    && IsAccessible(method)
                    && RuntimeTypesEqual(method.ReturnType, targetType)
                    && method.Parameters.All(x => x.RefKind == RefKind.None && !RuntimeTypesEqual(x.Type, targetType))
                )
                    yield return method;
            }
        }
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

    private static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated
        || type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsNullableValueType(ITypeSymbol type, out ITypeSymbol underlyingType)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            underlyingType = nullable.TypeArguments[0];
            return true;
        }

        underlyingType = null!;
        return false;
    }

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

    private static bool CanMutateCollection(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return false;
        return named
            .AllInterfaces.Append(named)
            .Any(x =>
                string.Equals(x.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.ICollection<T>", StringComparison.Ordinal)
                || string.Equals(
                    x.OriginalDefinition.ToDisplayString(),
                    "System.Collections.Generic.IDictionary<TKey, TValue>",
                    StringComparison.Ordinal
                )
            );
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
}
