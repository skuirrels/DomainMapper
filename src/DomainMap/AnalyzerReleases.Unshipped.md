; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/master/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DMAP001 | Domain boundary | Error | A required domain factory cannot bind all required parameters
DMAP002 | Domain boundary | Error | A required domain factory is used in a queryable projection
DMAP003 | Domain boundary | Error | A configured target-owned domain factory cannot be found or has an invalid signature
