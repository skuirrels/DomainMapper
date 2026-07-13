using System.Diagnostics.CodeAnalysis;
using DomainMap.Descriptors.Mappings.ExistingTarget;
using DomainMap.Symbols.Members;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Mappings.MemberMappings;

/// <summary>
/// A <see cref="IMemberAssignmentMapping"/> which maps to an existing target instance.
/// </summary>
public class MemberExistingTargetMapping(
    IExistingTargetMapping delegateMapping,
    MemberPathGetter sourcePath,
    MemberPathGetter targetPath,
    MemberMappingInfo memberInfo
) : IMemberAssignmentMapping
{
    public MemberMappingInfo MemberInfo { get; } = memberInfo;

    public bool TryGetMemberAssignmentMappingContainer([NotNullWhen(true)] out IMemberAssignmentMappingContainer? container)
    {
        container = delegateMapping as IMemberAssignmentMappingContainer;
        return container != null;
    }

    public IEnumerable<StatementSyntax> Build(TypeMappingBuildContext ctx, ExpressionSyntax targetAccess)
    {
        var source = sourcePath.BuildAccess(ctx.Source);
        var target = targetPath.BuildAccess(targetAccess);
        return delegateMapping.Build(ctx.WithSource(source), target);
    }
}
