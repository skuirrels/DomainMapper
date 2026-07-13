using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Mappings.MemberMappings;

public interface IAssignmentMappings
{
    IEnumerable<StatementSyntax> Build(TypeMappingBuildContext ctx, ExpressionSyntax targetAccess);
}
