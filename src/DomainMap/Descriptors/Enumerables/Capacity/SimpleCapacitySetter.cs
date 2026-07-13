using DomainMap.Descriptors.Mappings;
using DomainMap.Symbols.Members;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;

namespace DomainMap.Descriptors.Enumerables.Capacity;

/// <summary>
/// Represents setting the capacity on a collection where both the source and targets counts are accessible.
/// </summary>
/// <remarks>
/// <code>
/// target.EnsureCapacity(source.Length + target.Count);
/// // or
/// target.Capacity = source.Length + target.Count;
/// </code>
/// </remarks>
public class SimpleCapacitySetter(IMemberSetter capacitySetter, IMemberGetter? targetAccessor, IMemberGetter sourceAccessor)
    : ICapacitySetter
{
    public StatementSyntax Build(TypeMappingBuildContext ctx, ExpressionSyntax target)
    {
        var count = sourceAccessor.BuildAccess(ctx.Source);
        if (targetAccessor != null)
        {
            count = Add(count, targetAccessor.BuildAccess(target));
        }

        return ctx.SyntaxFactory.ExpressionStatement(capacitySetter.BuildAssignment(target, count));
    }
}
