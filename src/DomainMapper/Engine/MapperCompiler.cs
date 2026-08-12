using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

    private readonly INamedTypeSymbol _mapperType;
    private readonly Compilation _compilation;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<MappingContract> _rootContracts = [];
    private readonly List<MappingContract> _helperContracts = [];
    private readonly Queue<MappingRequest> _pendingHelpers = new();
    private readonly HashSet<string> _reservedHelpers = new(StringComparer.Ordinal);

    private MapperCompiler(INamedTypeSymbol mapperType, Compilation compilation)
    {
        _mapperType = mapperType;
        _compilation = compilation;
    }

    public static MapperCompilation Compile(INamedTypeSymbol mapperType, Compilation compilation, CancellationToken cancellationToken) =>
        new MapperCompiler(mapperType, compilation).Build(cancellationToken);

    private MapperCompilation Build(CancellationToken cancellationToken)
    {
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

        var source = _diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error) ? null : EmitSource();
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
        if (method.ReturnsVoid && method.Parameters.Length == 2)
        {
            BuildUpdateContract(method);
            return;
        }

        if (method.ReturnsVoid || method.Parameters.Length == 0)
        {
            _diagnostics.Add(Diagnostic.Create(UnsupportedMethod, method.Locations.FirstOrDefault(), method.Name));
            return;
        }

        var sourceParameter = method.Parameters[0];
        var factoryName = ReadFactoryName(method);
        var expression =
            factoryName == null
                ? BuildRootExpression(sourceParameter.Type, method.ReturnType, Escape(sourceParameter.Name))
                : BuildFactoryExpression(method.ReturnType, sourceParameter.Type, Escape(sourceParameter.Name), factoryName, method);

        if (expression == null)
            return;

        var body = $"var target = {expression};\nreturn target;";
        _rootContracts.Add(new MappingContract(method.Name, BuildDeclaration(method), body, MappingShape.Create));
    }

    private string? BuildRootExpression(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
            return sourceExpression;

        if (TryGetSequenceElement(sourceType, out _) && TryGetSequenceElement(targetType, out _))
            return ConvertExpression(sourceType, targetType, sourceExpression);

        return BuildObjectCreation(sourceType, targetType, sourceExpression) ?? ConvertExpression(sourceType, targetType, sourceExpression);
    }

    private void BuildUpdateContract(IMethodSymbol method)
    {
        var source = method.Parameters[0];
        var target = method.Parameters[1];
        var assignments = BuildAssignments(source.Type, target.Type, Escape(source.Name), Escape(target.Name));
        if (assignments == null)
        {
            ReportCannotConstruct(method, source.Type, target.Type);
            return;
        }

        _rootContracts.Add(new MappingContract(method.Name, BuildDeclaration(method), assignments, MappingShape.Update));
    }

    private void BuildHelperContract(MappingRequest request)
    {
        var expression = BuildObjectCreation(request.SourceType, request.TargetType, "source");
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

        var declaration = $"private static {TypeName(request.TargetType)} {request.MethodName}({TypeName(request.SourceType)} source)";
        _helperContracts.Add(
            new MappingContract(request.MethodName, declaration, $"var target = {expression};\nreturn target;", MappingShape.Helper)
        );
    }

    private string? ConvertExpression(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
            return sourceExpression;

        var conversionMethod = FindDomainConversion(sourceType, targetType);
        if (conversionMethod != null)
            return $"{Escape(conversionMethod.Name)}({sourceExpression})";

        if (
            TryGetDictionaryTypes(sourceType, out var sourceKey, out var sourceValue)
            && TryGetDictionaryTypes(targetType, out var targetKey, out var targetValue)
        )
        {
            return BuildDictionaryConversion(sourceType, targetType, sourceKey, sourceValue, targetKey, targetValue, sourceExpression);
        }

        if (TryGetSequenceElement(sourceType, out var sourceElement) && TryGetSequenceElement(targetType, out var targetElement))
        {
            return BuildSequenceConversion(sourceType, targetType, sourceElement, targetElement, sourceExpression);
        }

        var conversion = _compilation.ClassifyConversion(sourceType, targetType);
        if (conversion.Exists && conversion.IsImplicit)
            return sourceExpression;

        if (targetType is INamedTypeSymbol namedTarget)
        {
            var singleValueConstructor = namedTarget.InstanceConstructors.FirstOrDefault(x =>
                IsAccessible(x)
                && x.Parameters.Length == 1
                && _compilation.ClassifyConversion(sourceType, x.Parameters[0].Type) is { Exists: true, IsImplicit: true }
            );
            if (singleValueConstructor != null)
                return $"new {TypeName(targetType)}({sourceExpression})";
        }

        return QueueObjectHelper(sourceType, targetType, sourceExpression);
    }

    private string? BuildObjectCreation(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression)
    {
        if (targetType is not INamedTypeSymbol namedTarget)
            return null;

        var sourceMembers = ReadableProperties(sourceType);
        var constructors = namedTarget
            .InstanceConstructors.Where(IsAccessible)
            .Where(x => !IsRecordCopyConstructor(x, namedTarget))
            .OrderByDescending(x => x.Parameters.Length)
            .ToArray();

        foreach (var constructor in constructors.Where(x => x.Parameters.Length > 0))
        {
            var arguments = new List<string>();
            var valid = true;
            foreach (var parameter in constructor.Parameters)
            {
                if (!sourceMembers.TryGetValue(parameter.Name, out var sourceMember))
                {
                    valid = false;
                    break;
                }

                var argument = ConvertExpression(sourceMember.Type, parameter.Type, $"{sourceExpression}.{Escape(sourceMember.Name)}");
                if (argument == null)
                {
                    valid = false;
                    break;
                }

                arguments.Add(argument);
            }

            if (valid)
                return $"new {TypeName(targetType)}({string.Join(", ", arguments)})";
        }

        if (constructors.Any(x => x.Parameters.Length == 0))
        {
            var assignments = BuildAssignments(sourceType, targetType, sourceExpression, "target");
            if (assignments == null)
                return null;

            return new DeferredObjectCreation(TypeName(targetType), assignments).ToMarker();
        }

        return null;
    }

    private string? BuildAssignments(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression, string targetExpression)
    {
        var sourceMembers = ReadableProperties(sourceType);
        var lines = new List<string>();
        foreach (var targetMember in WritableProperties(targetType))
        {
            if (!sourceMembers.TryGetValue(targetMember.Name, out var sourceMember))
                continue;

            var value = ConvertExpression(sourceMember.Type, targetMember.Type, $"{sourceExpression}.{Escape(sourceMember.Name)}");
            if (value != null)
                lines.Add($"{targetExpression}.{Escape(targetMember.Name)} = {value};");
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private string? BuildFactoryExpression(
        ITypeSymbol targetType,
        ITypeSymbol sourceType,
        string sourceExpression,
        string factoryName,
        IMethodSymbol mappingMethod
    )
    {
        if (targetType is not INamedTypeSymbol namedTarget)
        {
            ReportCannotConstruct(mappingMethod, sourceType, targetType);
            return null;
        }

        var sourceMembers = ReadableProperties(sourceType);
        foreach (
            var factory in namedTarget
                .GetMembers(factoryName)
                .OfType<IMethodSymbol>()
                .Where(x => x.IsStatic && IsAccessible(x) && SymbolEqualityComparer.Default.Equals(x.ReturnType, targetType))
                .OrderByDescending(x => x.Parameters.Length)
        )
        {
            var arguments = new List<string>();
            var valid = true;
            foreach (var parameter in factory.Parameters)
            {
                if (!sourceMembers.TryGetValue(parameter.Name, out var sourceMember))
                {
                    valid = false;
                    break;
                }

                var argument = ConvertExpression(sourceMember.Type, parameter.Type, $"{sourceExpression}.{Escape(sourceMember.Name)}");
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

        ReportCannotConstruct(mappingMethod, sourceType, targetType);
        return null;
    }

    private string? BuildSequenceConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ITypeSymbol sourceElement,
        ITypeSymbol targetElement,
        string sourceExpression
    )
    {
        var helperName = $"MapTo{SequenceName(targetType, targetElement)}";
        var key = BuildHelperKey(sourceType, targetType);
        if (_reservedHelpers.Add(key))
        {
            var elementExpression = ConvertExpression(sourceElement, targetElement, "item");
            if (elementExpression == null)
                return null;

            var targetTypeName = TypeName(targetType);
            var sourceTypeName = TypeName(sourceType);
            var capacity = CountExpression(sourceType);
            if (targetType is IArrayTypeSymbol)
            {
                var arrayIteration =
                    $"for (var i = 0; i < {capacity}; i++)\n{{\n    var item = source[i];\n    target[i] = {elementExpression};\n}}";
                var arrayDeclaration = $"private static {targetTypeName} {helperName}({sourceTypeName} source)";
                _helperContracts.Add(
                    new MappingContract(
                        helperName,
                        arrayDeclaration,
                        $"var target = new {TypeName(targetElement)}[{capacity}];\n{arrayIteration}\nreturn target;",
                        MappingShape.Helper
                    )
                );
                return $"{helperName}({sourceExpression})";
            }

            var creation = BuildSequenceCreation(targetType, targetElement, capacity);
            var iteration = IsIndexable(sourceType)
                ? $"for (var i = 0; i < {capacity}; i++)\n{{\n    var item = source[i];\n    target.Add({elementExpression});\n}}"
                : $"foreach (var item in source)\n{{\n    target.Add({elementExpression});\n}}";
            var declaration = $"private static {targetTypeName} {helperName}({sourceTypeName} source)";
            _helperContracts.Add(
                new MappingContract(helperName, declaration, $"var target = {creation};\n{iteration}\nreturn target;", MappingShape.Helper)
            );
        }

        return $"{helperName}({sourceExpression})";
    }

    private string? BuildDictionaryConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ITypeSymbol sourceKey,
        ITypeSymbol sourceValue,
        ITypeSymbol targetKey,
        ITypeSymbol targetValue,
        string sourceExpression
    )
    {
        var helperName = $"MapToDictionaryOf{Sanitize(targetKey.Name)}And{Sanitize(targetValue.Name)}";
        var key = BuildHelperKey(sourceType, targetType);
        if (_reservedHelpers.Add(key))
        {
            var keyExpression = ConvertExpression(sourceKey, targetKey, "item.Key");
            var valueExpression = ConvertExpression(sourceValue, targetValue, "item.Value");
            if (keyExpression == null || valueExpression == null)
                return null;

            var declaration = $"private static {TypeName(targetType)} {helperName}({TypeName(sourceType)} source)";
            var creation = $"new {TypeName(targetType)}(source.Count)";
            var body =
                $"var target = {creation};\nforeach (var item in source)\n{{\n    target[{keyExpression}] = {valueExpression};\n}}\nreturn target;";
            _helperContracts.Add(new MappingContract(helperName, declaration, body, MappingShape.Helper));
        }

        return $"{helperName}({sourceExpression})";
    }

    private string? QueueObjectHelper(ITypeSymbol sourceType, ITypeSymbol targetType, string sourceExpression)
    {
        var helperName = $"MapTo{Sanitize(targetType.Name)}";
        var key = BuildHelperKey(sourceType, targetType);
        if (_reservedHelpers.Add(key))
            _pendingHelpers.Enqueue(new MappingRequest(sourceType, targetType, helperName));
        return $"{helperName}({sourceExpression})";
    }

    private IMethodSymbol? FindDomainConversion(ITypeSymbol sourceType, ITypeSymbol targetType) =>
        _mapperType
            .GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(x =>
                HasAttribute(x, DomainFactoryAttribute)
                && x.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(x.Parameters[0].Type, sourceType)
                && SymbolEqualityComparer.Default.Equals(x.ReturnType, targetType)
            );

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

        var accessibility = _mapperType.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var staticModifier = _mapperType.IsStatic ? " static" : string.Empty;
        writer.Line($"{accessibility}{staticModifier} partial class {Escape(_mapperType.Name)}");
        writer.Line("{");
        writer.Indent();

        var contracts = _rootContracts.Concat(_helperContracts).ToArray();
        for (var index = 0; index < contracts.Length; index++)
        {
            EmitContract(writer, contracts[index]);
            if (index < contracts.Length - 1)
                writer.Line();
        }

        writer.Unindent();
        writer.Line("}");
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
        var typeName = payload.Substring(0, separator);
        var assignments = payload.Substring(separator + 1).Replace("\\n", "\n", StringComparison.Ordinal);
        return $"var target = new {typeName}();\n{assignments}\nreturn target;";
    }

    private string BuildDeclaration(IMethodSymbol method)
    {
        var accessibility = AccessibilityText(method.DeclaredAccessibility);
        var staticModifier = method.IsStatic ? " static" : string.Empty;
        var parameters = string.Join(", ", method.Parameters.Select(x => $"{TypeName(x.Type)} {Escape(x.Name)}"));
        return $"{accessibility}{staticModifier} partial {TypeName(method.ReturnType)} {Escape(method.Name)}({parameters})";
    }

    private static string? ReadFactoryName(IMethodSymbol method)
    {
        var attribute = method
            .GetAttributes()
            .FirstOrDefault(x => string.Equals(x.AttributeClass?.ToDisplayString(), MapToFactoryAttribute, StringComparison.Ordinal));
        return attribute?.ConstructorArguments is [{ Value: string value }] ? value : null;
    }

    private static bool HasAttribute(IMethodSymbol method, string attributeName) =>
        method.GetAttributes().Any(x => string.Equals(x.AttributeClass?.ToDisplayString(), attributeName, StringComparison.Ordinal));

    private static Dictionary<string, IPropertySymbol> ReadableProperties(ITypeSymbol type) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(x => !x.IsStatic && x.GetMethod != null && IsAccessible(x.GetMethod))
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<IPropertySymbol> WritableProperties(ITypeSymbol type) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(x => !x.IsStatic && x.SetMethod != null && !x.SetMethod.IsInitOnly && IsAccessible(x.SetMethod));

    private static bool IsAccessible(ISymbol symbol) =>
        symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool IsRecordCopyConstructor(IMethodSymbol constructor, INamedTypeSymbol targetType) =>
        targetType.IsRecord
        && constructor.Parameters is [{ Type: var parameterType }]
        && SymbolEqualityComparer.Default.Equals(parameterType, targetType);

    private static bool TryGetSequenceElement(ITypeSymbol type, out ITypeSymbol elementType)
    {
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
            var dictionary = named
                .AllInterfaces.Append(named)
                .FirstOrDefault(x =>
                    string.Equals(
                        x.OriginalDefinition.ToDisplayString(),
                        "System.Collections.Generic.IDictionary<TKey, TValue>",
                        StringComparison.Ordinal
                    )
                );
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

    private static bool IsIndexable(ITypeSymbol type) =>
        type is IArrayTypeSymbol
        || (
            type.GetMembers().OfType<IPropertySymbol>().Any(x => x.IsIndexer && x.Parameters.Length == 1)
            && type.GetMembers("Count").OfType<IPropertySymbol>().Any()
        );

    private static string CountExpression(ITypeSymbol type) => type is IArrayTypeSymbol ? "source.Length" : "source.Count";

    private static string BuildSequenceCreation(ITypeSymbol targetType, ITypeSymbol targetElement, string capacity)
    {
        var listType = $"global::System.Collections.Generic.List<{TypeName(targetElement)}>";
        var targetIsList =
            targetType is INamedTypeSymbol named
            && string.Equals(named.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.List<T>", StringComparison.Ordinal);
        var constructedType = targetIsList ? TypeName(targetType) : listType;
        return string.IsNullOrEmpty(capacity) ? $"new {constructedType}()" : $"new {constructedType}({capacity})";
    }

    private static string SequenceName(ITypeSymbol targetType, ITypeSymbol targetElement)
    {
        if (targetType is INamedTypeSymbol named && string.Equals(named.Name, "List", StringComparison.Ordinal))
            return $"ListOf{Sanitize(targetElement.Name)}";
        return $"SequenceOf{Sanitize(targetElement.Name)}";
    }

    private static string BuildHelperKey(ITypeSymbol sourceType, ITypeSymbol targetType) =>
        $"{sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}->{targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";

    private static string BuildHintName(INamedTypeSymbol mapperType) => $"{Sanitize(mapperType.Name)}.g.cs";

    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string AccessibilityText(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "private",
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
        public DeferredObjectCreation(string typeName, string assignments)
        {
            TypeName = typeName;
            Assignments = assignments;
        }

        public string TypeName { get; }

        public string Assignments { get; }

        public string ToMarker() => $"__DOMAINMAPPER_CREATE__({TypeName}|{Assignments.Replace("\n", "\\n", StringComparison.Ordinal)})__";
    }
}
