using DomainMap.Abstractions;
using DomainMap.Descriptors;

namespace DomainMap.Configuration;

public record MappingConfiguration(
    DomainMapperAttribute Mapper,
    string? TargetFactoryMethodName,
    EnumMappingConfiguration Enum,
    MembersMappingConfiguration Members,
    IReadOnlyCollection<DerivedTypeMappingConfiguration> DerivedTypes,
    bool UseDeepCloning,
    StackCloningStrategy StackCloningStrategy,
    SupportedFeatures SupportedFeatures
)
{
    public MappingConfiguration Include(MappingConfiguration? otherConfiguration)
    {
        return this with
        {
            Enum = Enum.Include(otherConfiguration?.Enum),
            Members = Members.Include(otherConfiguration?.Members),
            DerivedTypes = DerivedTypes.Concat(otherConfiguration?.DerivedTypes ?? []).ToList(),
        };
    }

    public IgnoreObsoleteMembersStrategy GetIgnoreObsoleteMembersStrategy()
    {
        return Members.IgnoreObsoleteMembersStrategy.GetValueOrDefault(Mapper.IgnoreObsoleteMembersStrategy);
    }

    public bool HasRequiredMappingStrategyForMembers(RequiredMappingStrategy flag)
    {
        return Members.RequiredMappingStrategy.GetValueOrDefault(Mapper.RequiredMappingStrategy).HasFlag(flag);
    }
}
