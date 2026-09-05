; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/master/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DMPR108 | DomainMapper | Warning | Convention construction bypasses a static factory declared by the target type
DMPR109 | DomainMapper | Error | Enum pairs map by member name and reject unmatched, aliased, or flags members
