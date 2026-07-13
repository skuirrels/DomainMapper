using DomainMap.Descriptors.Constructors;
using DomainMap.Descriptors.Enumerables;
using DomainMap.Descriptors.Enumerables.Capacity;
using DomainMap.Descriptors.Mappings.ExistingTarget;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.Mappings;

/// <summary>
/// Represents a foreach dictionary mapping which works by creating a new target instance,
/// looping through the source, mapping each element and setting it to the target collection.
/// </summary>
public class ForEachSetDictionaryMapping : NewInstanceObjectMemberMethodMapping, INewInstanceEnumerableMapping
{
    private readonly ForEachSetDictionaryExistingTargetMapping _existingTargetMapping;

    public ForEachSetDictionaryMapping(
        IInstanceConstructor? constructor,
        CollectionInfos collectionInfos,
        INewInstanceMapping keyMapping,
        INewInstanceMapping valueMapping,
        INamedTypeSymbol? explicitCast,
        bool enableReferenceHandling
    )
        : base(collectionInfos.Source.Type, collectionInfos.Target.Type, enableReferenceHandling)
    {
        _existingTargetMapping = new(collectionInfos, keyMapping, valueMapping, explicitCast);
        if (constructor != null)
        {
            Constructor = constructor;
        }
    }

    public CollectionInfos CollectionInfos => _existingTargetMapping.CollectionInfos;

    public void AddCapacitySetter(ICapacitySetter capacitySetter) => _existingTargetMapping.AddCapacitySetter(capacitySetter);

    protected override IEnumerable<StatementSyntax> BuildBody(TypeMappingBuildContext ctx, ExpressionSyntax target)
    {
        return base.BuildBody(ctx, target).Concat(_existingTargetMapping.Build(ctx, target));
    }
}
