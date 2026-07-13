using System.Diagnostics;
using DomainMap.Configuration.MethodReferences;
using DomainMap.Configuration.PropertyReferences;
using DomainMap.Descriptors;

namespace DomainMap.Configuration;

[DebuggerDisplay("{Source} => {Target}")]
public record MemberMappingConfiguration(IMemberPathConfiguration Source, IMemberPathConfiguration Target) : HasSyntaxReference
{
    /// <summary>
    /// Used to adapt from <see cref="Abstractions.MapPropertyFromSourceAttribute"/>
    /// </summary>
    public MemberMappingConfiguration(IMemberPathConfiguration Target)
        : this(Source: StringMemberPath.Empty, Target) { }

    public string? StringFormat { get; set; }

    public string? FormatProvider { get; set; }

    public IMethodReferenceConfiguration? Use { get; set; }

    public bool SuppressNullMismatchDiagnostic { get; set; }

    public bool IsValid => Use == null || FormatProvider == null && StringFormat == null;

    public TypeMappingConfiguration ToTypeMappingConfiguration() =>
        new(StringFormat, FormatProvider, Use?.FullName, SuppressNullMismatchDiagnostic);
}
