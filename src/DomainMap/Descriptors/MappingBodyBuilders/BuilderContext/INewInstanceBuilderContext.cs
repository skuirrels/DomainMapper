using System.Diagnostics.CodeAnalysis;
using DomainMap.Descriptors.Mappings;
using DomainMap.Descriptors.Mappings.MemberMappings;
using DomainMap.Symbols.Members;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.MappingBodyBuilders.BuilderContext;

/// <summary>
/// A <see cref="IMembersBuilderContext{T}"/> for mappings which create the target object via new ...().
/// </summary>
/// <typeparam name="T">The mapping type.</typeparam>
public interface INewInstanceBuilderContext<out T> : IMembersBuilderContext<T>
    where T : IMapping
{
    bool TryMatchParameter(IParameterSymbol parameter, [NotNullWhen(true)] out MemberMappingInfo? memberInfo);

    bool TryMatchInitOnlyMember(IMappableMember targetMember, [NotNullWhen(true)] out MemberMappingInfo? memberInfo);

    void AddConstructorParameterMapping(ConstructorParameterMapping mapping);

    void AddInitMemberMapping(MemberAssignmentMapping mapping);
}
