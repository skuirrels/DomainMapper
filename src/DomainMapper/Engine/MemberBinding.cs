using System.Collections.Immutable;

namespace DomainMapper.Engine;

internal sealed class MemberBinding
{
    public MemberBinding(string targetMember, string sourcePath, ImmutableArray<MappingMember> sourceMembers)
    {
        TargetMember = targetMember;
        SourcePath = sourcePath;
        SourceMembers = sourceMembers;
    }

    public string TargetMember { get; }

    public string SourcePath { get; }

    public ImmutableArray<MappingMember> SourceMembers { get; }

    public MappingMember Leaf => SourceMembers[^1];
}
