using DomainMap.Helpers;
using Microsoft.CodeAnalysis;

namespace DomainMap.Output;

public readonly record struct MapperAndDiagnostics(MapperNode Mapper, ImmutableEquatableArray<Diagnostic> Diagnostics);
