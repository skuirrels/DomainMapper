using System.Diagnostics;

namespace DomainMapper.Abstractions;

/// <summary>
/// Binds a target member to a readable source member or nested source path for one mapping method.
/// Use <see cref="System.Linq.Expressions.Expression"/>-free <c>nameof</c> values so declarations remain compile-time only.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[Conditional("DOMAINMAPPER_ABSTRACTIONS_SCOPE_RUNTIME")]
public sealed class MapMemberAttribute(string targetMember, string sourcePath) : Attribute
{
    /// <summary>The target property or field name.</summary>
    public string TargetMember { get; } = targetMember;

    /// <summary>A dot-separated readable source path.</summary>
    public string SourcePath { get; } = sourcePath;
}
