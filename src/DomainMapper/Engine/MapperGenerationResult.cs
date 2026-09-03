using System.Collections.Immutable;

namespace DomainMapper.Engine;

/// <summary>
/// The complete output for one mapper type. This is the value cached by the incremental pipeline, so it holds
/// only strings and plain data: equal results let the driver skip re-emitting an unchanged mapper.
/// </summary>
internal sealed class MapperGenerationResult : IEquatable<MapperGenerationResult>
{
    public MapperGenerationResult(string hintName, string? source, ImmutableArray<DiagnosticData> diagnostics)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
    }

    public string HintName { get; }

    public string? Source { get; }

    public ImmutableArray<DiagnosticData> Diagnostics { get; }

    public bool Equals(MapperGenerationResult? other) =>
        other != null
        && string.Equals(HintName, other.HintName, StringComparison.Ordinal)
        && string.Equals(Source, other.Source, StringComparison.Ordinal)
        && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as MapperGenerationResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(HintName, StringComparer.Ordinal);
        hash.Add(Source, StringComparer.Ordinal);
        foreach (var diagnostic in Diagnostics)
            hash.Add(diagnostic);
        return hash.ToHashCode();
    }
}
