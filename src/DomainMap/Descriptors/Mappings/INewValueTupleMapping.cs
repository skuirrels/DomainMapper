using DomainMap.Descriptors.Mappings.MemberMappings;

namespace DomainMap.Descriptors.Mappings;

/// <summary>
/// A tuple mapping creating the target instance via a tuple expression (eg. (A: 10, B: 20)).
/// </summary>
public interface INewValueTupleMapping : INewInstanceMapping
{
    void AddConstructorParameterMapping(ValueTupleConstructorParameterMapping mapping);
}
