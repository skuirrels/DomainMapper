using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DomainMapper.Engine;

/// <summary>
/// A diagnostic captured as plain data so the incremental pipeline can cache it without retaining
/// the compilation, syntax tree, or symbols it was reported against.
/// </summary>
internal sealed class DiagnosticData : IEquatable<DiagnosticData>
{
    private DiagnosticData(string id, string? filePath, TextSpan span, LinePositionSpan lineSpan, ImmutableArray<string> messageArguments)
    {
        Id = id;
        FilePath = filePath;
        Span = span;
        LineSpan = lineSpan;
        MessageArguments = messageArguments;
    }

    public string Id { get; }

    public string? FilePath { get; }

    public TextSpan Span { get; }

    public LinePositionSpan LineSpan { get; }

    public ImmutableArray<string> MessageArguments { get; }

    public static DiagnosticData Create(DiagnosticDescriptor descriptor, Location? location, params string[] messageArguments)
    {
        if (location is { IsInSource: true, SourceTree: { } tree })
            return new DiagnosticData(
                descriptor.Id,
                tree.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span,
                ImmutableArray.Create(messageArguments)
            );
        return new DiagnosticData(descriptor.Id, null, default, default, ImmutableArray.Create(messageArguments));
    }

    public Diagnostic ToDiagnostic()
    {
        var location = FilePath == null ? Location.None : Location.Create(FilePath, Span, LineSpan);
        return Diagnostic.Create(MapperDiagnostics.Get(Id), location, MessageArguments.ToArray<object>());
    }

    public bool Equals(DiagnosticData? other) =>
        other != null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
        && Span == other.Span
        && LineSpan == other.LineSpan
        && MessageArguments.SequenceEqual(other.MessageArguments, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DiagnosticData);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(FilePath, StringComparer.Ordinal);
        hash.Add(Span);
        hash.Add(LineSpan);
        foreach (var argument in MessageArguments)
            hash.Add(argument, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
