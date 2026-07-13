using DomainMap.Descriptors.Enumerables;
using DomainMap.Descriptors.Enumerables.Capacity;
using DomainMap.Descriptors.Mappings.MemberMappings;

namespace DomainMap.Descriptors.Mappings;

public interface IEnumerableMapping : IMemberAssignmentTypeMapping
{
    CollectionInfos CollectionInfos { get; }

    void AddCapacitySetter(ICapacitySetter capacitySetter);
}
