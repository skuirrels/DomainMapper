using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed class MapperCompilation
{
    public MapperCompilation(string hintName, string? source, ImmutableArray<Diagnostic> diagnostics)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
    }

    public string HintName { get; }

    public string? Source { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
