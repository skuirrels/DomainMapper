using DomainMap.Descriptors.Mappings.ExistingTarget;
using DomainMap.Descriptors.Mappings.MemberMappings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Mappings;

/// <summary>
/// A mapping from type to another by mapping each property.
/// A <see cref="MethodMapping"/> implementation of <see cref="IMemberAssignmentTypeMapping"/>.
/// </summary>
public abstract class ObjectMemberMethodMapping(ITypeSymbol sourceType, ITypeSymbol targetType)
    : NewInstanceMethodMapping(sourceType, targetType),
        IMemberAssignmentTypeMapping
{
    private readonly ObjectMemberExistingTargetMapping _mapping = new(sourceType, targetType);

    public bool HasMemberMapping(IMemberAssignmentMapping mapping) => _mapping.HasMemberMapping(mapping);

    public void AddMemberMapping(IMemberAssignmentMapping mapping) => _mapping.AddMemberMapping(mapping);

    public bool HasMemberMappingContainer(IMemberAssignmentMappingContainer container) => _mapping.HasMemberMappingContainer(container);

    public void AddMemberMappingContainer(IMemberAssignmentMappingContainer container) => _mapping.AddMemberMappingContainer(container);

    public IEnumerable<StatementSyntax> Build(TypeMappingBuildContext ctx, ExpressionSyntax targetAccess) => BuildBody(ctx, targetAccess);

    protected virtual IEnumerable<StatementSyntax> BuildBody(TypeMappingBuildContext ctx, ExpressionSyntax target) =>
        _mapping.Build(ctx, target);
}
