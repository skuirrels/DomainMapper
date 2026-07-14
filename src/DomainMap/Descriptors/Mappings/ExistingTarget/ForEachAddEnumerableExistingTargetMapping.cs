using DomainMap.Descriptors.Enumerables;
using DomainMap.Descriptors.Enumerables.Capacity;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Descriptors.Mappings.ExistingTarget;

/// <summary>
/// Represents a foreach enumerable mapping which works by looping through the source,
/// mapping each element and adding it to the target collection.
/// </summary>
public class ForEachAddEnumerableExistingTargetMapping(
    CollectionInfos collectionInfos,
    INewInstanceMapping elementMapping,
    string insertMethodName
) : ObjectMemberExistingTargetMapping(collectionInfos.Source.Type, collectionInfos.Target.Type), IEnumerableMapping
{
    private const string LoopItemVariableName = "item";

    private ICapacitySetter? _capacitySetter;

    public CollectionInfos CollectionInfos => collectionInfos;

    public void AddCapacitySetter(ICapacitySetter capacitySetter) => _capacitySetter = capacitySetter;

    public override IEnumerable<StatementSyntax> Build(TypeMappingBuildContext ctx, ExpressionSyntax target)
    {
        foreach (var statement in base.Build(ctx, target))
        {
            yield return statement;
        }

        if (_capacitySetter != null)
        {
            yield return _capacitySetter.Build(ctx, target);
        }

        var addMethod = MemberAccess(target, insertMethodName);
        if (collectionInfos.Source.CollectionType == CollectionType.List)
        {
            var counterName = ctx.NameBuilder.New("i");
            var sourceItem = ElementAccess(ctx.Source, IdentifierName(counterName));
            var (indexedItemCtx, indexedItemVariableName) = ctx.AddIndentation().WithNewSource(LoopItemVariableName);
            var declareLoopItem = indexedItemCtx.SyntaxFactory.DeclareLocalVariable(indexedItemVariableName, sourceItem);
            var convertedSourceItemExpression = elementMapping.Build(indexedItemCtx);
            var addLoopItem = indexedItemCtx.SyntaxFactory.ExpressionStatement(
                ctx.SyntaxFactory.Invocation(addMethod, convertedSourceItemExpression)
            );
            var count = MemberAccess(ctx.Source, collectionInfos.Source.CountMember!.Name);
            yield return ctx.SyntaxFactory.IncrementalForLoop(counterName, count, [declareLoopItem, addLoopItem]);
            yield break;
        }

        var (loopItemCtx, loopItemVariableName) = ctx.WithNewSource(LoopItemVariableName);
        var convertedLoopItemExpression = elementMapping.Build(loopItemCtx);
        var loopBody = ctx.SyntaxFactory.Invocation(addMethod, convertedLoopItemExpression);
        yield return ctx.SyntaxFactory.ForEach(loopItemVariableName, ctx.Source, loopBody);
    }
}
