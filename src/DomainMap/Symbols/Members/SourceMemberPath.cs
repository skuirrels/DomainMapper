using System.Diagnostics;

namespace DomainMap.Symbols.Members;

[DebuggerDisplay("{MemberPath} ({Type})")]
public record SourceMemberPath(MemberPath MemberPath, SourceMemberType Type);
