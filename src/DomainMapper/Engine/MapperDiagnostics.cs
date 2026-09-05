using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

/// <summary>The diagnostics DomainMapper reports, addressable by identifier so cached diagnostic data can be rehydrated.</summary>
internal static class MapperDiagnostics
{
    internal static readonly DiagnosticDescriptor UnsupportedMethod = new(
        "DMPR100",
        "Mapping contract is not supported",
        "Method '{0}' does not match a supported DomainMapper mapping contract",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    internal static readonly DiagnosticDescriptor CannotConstruct = new(
        "DMPR101",
        "Target cannot be constructed",
        "DomainMapper cannot construct '{0}' from '{1}'",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    internal static readonly DiagnosticDescriptor InvalidConfiguration = new(
        "DMPR102",
        "Mapping configuration is invalid",
        "Mapping '{0}' configuration is invalid: {1}",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    internal static readonly DiagnosticDescriptor IncompleteSource = new(
        "DMPR103",
        "Source mapping is incomplete",
        "Mapping '{0}' does not consume or ignore source member '{1}'",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    internal static readonly DiagnosticDescriptor CompletenessDisabled = new(
        "DMPR104",
        "Mapping completeness is disabled",
        "Mapping '{0}' explicitly disables source and target completeness validation",
        "DomainMapper",
        DiagnosticSeverity.Warning,
        true
    );

    internal static readonly DiagnosticDescriptor UnsupportedReferenceTracking = new(
        "DMPR105",
        "Reference tracking target is not supported",
        "Mapping '{0}' cannot preserve references: {1}",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    internal static readonly DiagnosticDescriptor InvalidRegistry = new(
        "DMPR107",
        "Runtime registry is invalid",
        "Mapper '{0}' runtime registry is invalid: {1}",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    internal static readonly DiagnosticDescriptor InvalidProjection = new(
        "DMPR106",
        "Projection contract is not supported",
        "Projection '{0}' for member '{1}' cannot use {2}; {3}",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    internal static readonly DiagnosticDescriptor FactoryBypassed = new(
        "DMPR108",
        "Target factory bypassed",
        "Mapping '{0}' constructs '{1}' through its constructor although the type declares static factory method(s) {2}; route construction through [MapToFactory] or a [DomainFactory], or declare [IgnoreTargetFactory(typeof({1}))] with a reason",
        "DomainMapper",
        DiagnosticSeverity.Warning,
        true
    );

    internal static readonly DiagnosticDescriptor EnumNotMapped = new(
        "DMPR109",
        "Enum cannot be mapped by member name",
        "Mapping '{0}' cannot map enum '{1}' to '{2}' by member name: {3}",
        "DomainMapper",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly ImmutableDictionary<string, DiagnosticDescriptor> ByIdentifier = new[]
    {
        UnsupportedMethod,
        CannotConstruct,
        InvalidConfiguration,
        IncompleteSource,
        CompletenessDisabled,
        UnsupportedReferenceTracking,
        InvalidRegistry,
        InvalidProjection,
        FactoryBypassed,
        EnumNotMapped,
    }.ToImmutableDictionary(x => x.Id, StringComparer.Ordinal);

    public static DiagnosticDescriptor Get(string id) =>
        ByIdentifier.TryGetValue(id, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown DomainMapper diagnostic identifier.");
}
