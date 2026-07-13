using DomainMap.Descriptors.Mappings;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Enumerables.Capacity;

/// <summary>
/// Sets the capacity of a collection to the calculated count.
/// </summary>
public interface ICapacitySetter
{
    StatementSyntax Build(TypeMappingBuildContext ctx, ExpressionSyntax target);
}
