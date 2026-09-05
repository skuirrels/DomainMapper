using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed partial class MapperCompiler
{
    private const string DomainFactoryAttribute = "DomainMapper.Abstractions.DomainFactoryAttribute";
    private const string IgnoreSourceMemberAttribute = "DomainMapper.Abstractions.IgnoreSourceMemberAttribute";
    private const string IgnoreTargetMemberAttribute = "DomainMapper.Abstractions.IgnoreTargetMemberAttribute";
    private const string IgnoreTargetFactoryAttribute = "DomainMapper.Abstractions.IgnoreTargetFactoryAttribute";
    private const string IncludeMappingAttribute = "DomainMapper.Abstractions.IncludeMappingAttribute";
    private const string MapCollectionAttribute = "DomainMapper.Abstractions.MapCollectionAttribute";
    private const string MapConditionAttribute = "DomainMapper.Abstractions.MapConditionAttribute";
    private const string MapAfterAttribute = "DomainMapper.Abstractions.MapAfterAttribute";
    private const string MapMemberAttribute = "DomainMapper.Abstractions.MapMemberAttribute";
    private const string MapMaxDepthAttribute = "DomainMapper.Abstractions.MapMaxDepthAttribute";
    private const string MapNullAttribute = "DomainMapper.Abstractions.MapNullAttribute";
    private const string MapNullSubstituteAttribute = "DomainMapper.Abstractions.MapNullSubstituteAttribute";
    private const string MapOnlyTargetMembersAttribute = "DomainMapper.Abstractions.MapOnlyTargetMembersAttribute";
    private const string MapReferenceTrackingAttribute = "DomainMapper.Abstractions.MapReferenceTrackingAttribute";
    private const string MapRegistryAttribute = "DomainMapper.Abstractions.MapRegistryAttribute";
    private const string MapRegistryDerivedAttribute = "DomainMapper.Abstractions.MapRegistryDerivedAttribute";
    private const string MapTargetMemberAttribute = "DomainMapper.Abstractions.MapTargetMemberAttribute";
    private const string MapToFactoryAttribute = "DomainMapper.Abstractions.MapToFactoryAttribute";
    private const string MappingCompletenessAttribute = "DomainMapper.Abstractions.MappingCompletenessAttribute";
    private const string MapProjectionAttribute = "DomainMapper.Projections.MapProjectionAttribute";
    private const string SetsRequiredMembersAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";

    private static readonly string[] ExplicitConfigurationAttributes =
    [
        IgnoreSourceMemberAttribute,
        IgnoreTargetMemberAttribute,
        IncludeMappingAttribute,
        MapMemberAttribute,
        MapCollectionAttribute,
        MapMaxDepthAttribute,
        MapNullAttribute,
        MapNullSubstituteAttribute,
        MapOnlyTargetMembersAttribute,
        MapReferenceTrackingAttribute,
        MappingCompletenessAttribute,
    ];

    private static readonly string GeneratedCodeAttribute =
        $"[global::System.CodeDom.Compiler.GeneratedCode(\"DomainMapper\", \"{typeof(MapperCompiler).Assembly.GetName().Version?.ToString() ?? "0.0.0.0"}\")]";

    private static readonly SymbolDisplayFormat TypeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
    );

    private readonly INamedTypeSymbol _mapperType;
    private readonly Compilation _compilation;
    private readonly List<DiagnosticData> _diagnostics = [];
    private readonly List<MappingContract> _rootContracts = [];
    private readonly List<MappingContract> _helperContracts = [];
    private readonly Queue<MappingRequest> _pendingHelpers = new();
    private readonly Dictionary<string, string> _helperNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedHelperNames = new(StringComparer.Ordinal);
    private readonly HashSet<IMethodSymbol> _activeDomainFactories = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, IReadOnlyList<MappingMember>> _mappingMembers = new(SymbolEqualityComparer.Default);
    private readonly ImmutableArray<IMethodSymbol> _mappingMethods;
    private readonly ImmutableArray<IMethodSymbol> _projectionMethods;
    private readonly IReadOnlyDictionary<string, ImmutableArray<IMethodSymbol>> _configurationHelpers;
    private readonly List<string> _supportMembers = [];
    private readonly Dictionary<IMethodSymbol, MappingMethodConfiguration> _configurations = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> _successfulMappingMethods = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> _declaredMappingReuse = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> _reportedAmbiguousReuse = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedFactoryBypass = new(StringComparer.Ordinal);
    private readonly HashSet<AttributeData> _consumedFactoryIgnores = [];
    private readonly Dictionary<string, ImmutableArray<INamedTypeSymbol>> _attributeTypes = new(StringComparer.Ordinal);
    private string? _referenceKeyName;

    private MapperCompiler(INamedTypeSymbol mapperType, Compilation compilation)
    {
        _mapperType = mapperType;
        _compilation = compilation;
        var partialMethods = mapperType
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(x => x.IsPartialDefinition && x.PartialImplementationPart == null)
            .OrderBy(x => x.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .ToImmutableArray();
        _projectionMethods = partialMethods.Where(x => HasAttribute(x, MapProjectionAttribute)).ToImmutableArray();
        _mappingMethods = partialMethods.Where(x => !HasAttribute(x, MapProjectionAttribute)).ToImmutableArray();
        foreach (var memberName in GetTypeHierarchy(mapperType).SelectMany(x => x.GetMembers()).Select(x => x.Name))
        {
            _usedHelperNames.Add(memberName);
        }
        for (var baseType = mapperType.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            foreach (
                var memberName in baseType.GetMembers().Where(x => x.DeclaredAccessibility != Accessibility.Private).Select(x => x.Name)
            )
            {
                _usedHelperNames.Add(memberName);
            }
        }
        _configurationHelpers = IndexConfigurationHelpers(mapperType);
    }

    public static MapperGenerationResult Compile(
        INamedTypeSymbol mapperType,
        Compilation compilation,
        CancellationToken cancellationToken
    ) => new MapperCompiler(mapperType, compilation).Build(cancellationToken);

    private MapperGenerationResult Build(CancellationToken cancellationToken)
    {
        if (GetTypeHierarchy(_mapperType).Any(x => x.IsFileLocal))
        {
            foreach (var method in _mappingMethods)
            {
                ReportUnsupported(method);
            }

            return new MapperGenerationResult(BuildHintName(_mapperType), null, _diagnostics.ToImmutableArray());
        }

        ValidateDomainFactories();
        ValidateConfigurationHelpers();

        foreach (var method in _mappingMethods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildRootContract(method);
        }

        while (_pendingHelpers.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildHelperContract(_pendingHelpers.Dequeue());
        }

        RejectDeclaredMappingCycles();
        ValidateFactoryIgnores();
        BuildProjections();
        BuildRuntimeRegistry();

        var source = _rootContracts.Count == 0 && _supportMembers.Count == 0 ? null : EmitSource();
        return new MapperGenerationResult(BuildHintName(_mapperType), source, _diagnostics.ToImmutableArray());
    }

    private void ReportUnsupported(IMethodSymbol method) =>
        _diagnostics.Add(DiagnosticData.Create(MapperDiagnostics.UnsupportedMethod, method.Locations.FirstOrDefault(), method.Name));

    private void ReportCannotConstruct(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol targetType) =>
        _diagnostics.Add(
            DiagnosticData.Create(
                MapperDiagnostics.CannotConstruct,
                method.Locations.FirstOrDefault(),
                targetType.ToDisplayString(),
                sourceType.ToDisplayString()
            )
        );
}
