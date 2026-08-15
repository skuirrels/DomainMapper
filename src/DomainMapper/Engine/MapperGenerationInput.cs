using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMapper.Engine;

internal sealed class MapperGenerationInput
{
    private MapperGenerationInput(INamedTypeSymbol mapperType, Compilation compilation, ulong fingerprint)
    {
        MapperType = mapperType;
        Compilation = compilation;
        Fingerprint = fingerprint;
    }

    public INamedTypeSymbol MapperType { get; }

    public Compilation Compilation { get; }

    public ulong Fingerprint { get; }

    public static MapperGenerationInput Create(INamedTypeSymbol mapperType, Compilation compilation)
    {
        var fingerprint = new FingerprintBuilder();
        fingerprint.Add(mapperType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        fingerprint.Add(compilation.AssemblyName ?? string.Empty);
        fingerprint.Add(compilation.Options.OutputKind.ToString());
        if (compilation.Options is CSharpCompilationOptions compilationOptions)
        {
            fingerprint.Add(compilationOptions.NullableContextOptions.ToString());
            fingerprint.Add(compilationOptions.AllowUnsafe ? "unsafe" : "safe");
            fingerprint.Add(compilationOptions.CheckOverflow ? "checked" : "unchecked");
        }
        var sourceTrees = new HashSet<SyntaxTree>();
        for (var containingType = mapperType; containingType != null; containingType = containingType.ContainingType)
        {
            foreach (
                var syntax in containingType
                    .DeclaringSyntaxReferences.OrderBy(x => x.SyntaxTree.FilePath, StringComparer.Ordinal)
                    .ThenBy(x => x.Span.Start)
            )
                AddSyntaxTree(syntax.SyntaxTree, ref fingerprint, sourceTrees);
        }

        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        if (mapperType.BaseType != null)
            AppendSourceType(mapperType.BaseType, ref fingerprint, visited, sourceTrees);
        foreach (var method in mapperType.GetMembers().OfType<IMethodSymbol>())
        {
            AppendSourceType(method.ReturnType, ref fingerprint, visited, sourceTrees);
            foreach (var parameter in method.Parameters)
                AppendSourceType(parameter.Type, ref fingerprint, visited, sourceTrees);
        }
        return new MapperGenerationInput(mapperType, compilation, fingerprint.Value);
    }

    private static void AppendSourceType(
        ITypeSymbol type,
        ref FingerprintBuilder fingerprint,
        ISet<ITypeSymbol> visited,
        ISet<SyntaxTree> sourceTrees
    )
    {
        if (type is IArrayTypeSymbol array)
        {
            AppendSourceType(array.ElementType, ref fingerprint, visited, sourceTrees);
            return;
        }
        if (type is not INamedTypeSymbol named || !visited.Add(named))
            return;
        foreach (var argument in named.TypeArguments)
            AppendSourceType(argument, ref fingerprint, visited, sourceTrees);
        if (!named.Locations.Any(x => x.IsInSource))
        {
            fingerprint.Add(named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            fingerprint.Add(named.ContainingAssembly.Identity.ToString());
            return;
        }

        foreach (
            var syntax in named
                .DeclaringSyntaxReferences.OrderBy(x => x.SyntaxTree.FilePath, StringComparer.Ordinal)
                .ThenBy(x => x.Span.Start)
        )
            AddSyntaxTree(syntax.SyntaxTree, ref fingerprint, sourceTrees);

        foreach (var member in named.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol property when !property.IsStatic:
                    AppendSourceType(property.Type, ref fingerprint, visited, sourceTrees);
                    break;
                case IFieldSymbol field when !field.IsStatic:
                    AppendSourceType(field.Type, ref fingerprint, visited, sourceTrees);
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                    foreach (var parameter in constructor.Parameters)
                        AppendSourceType(parameter.Type, ref fingerprint, visited, sourceTrees);
                    break;
            }
        }
        if (named.BaseType != null)
            AppendSourceType(named.BaseType, ref fingerprint, visited, sourceTrees);
    }

    private static void AddSyntaxTree(SyntaxTree tree, ref FingerprintBuilder fingerprint, ISet<SyntaxTree> sourceTrees)
    {
        if (!sourceTrees.Add(tree))
            return;
        fingerprint.Add(tree.FilePath);
        fingerprint.Add(tree.GetText().GetChecksum());
        if (tree.Options is CSharpParseOptions parseOptions)
        {
            fingerprint.Add(parseOptions.LanguageVersion.ToString());
            fingerprint.Add(parseOptions.DocumentationMode.ToString());
            fingerprint.Add(parseOptions.Kind.ToString());
            foreach (var symbol in parseOptions.PreprocessorSymbolNames.OrderBy(x => x, StringComparer.Ordinal))
                fingerprint.Add(symbol);
        }
    }

    private struct FingerprintBuilder
    {
        private const ulong Offset = 14695981039346656037;
        private const ulong Prime = 1099511628211;

        private ulong _value;

        public readonly ulong Value => _value == 0 ? Offset : _value;

        public void Add(string value)
        {
            if (_value == 0)
                _value = Offset;
            foreach (var character in value)
            {
                _value ^= character;
                _value *= Prime;
            }
            _value ^= 0xFF;
            _value *= Prime;
        }

        public void Add(IEnumerable<byte> value)
        {
            if (_value == 0)
                _value = Offset;
            foreach (var item in value)
            {
                _value ^= item;
                _value *= Prime;
            }
            _value ^= 0xFE;
            _value *= Prime;
        }
    }
}
