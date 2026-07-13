using System.Diagnostics.CodeAnalysis;
using DomainMap.Descriptors.Mappings;
using DomainMap.Descriptors.Mappings.MemberMappings;
using DomainMap.Diagnostics;
using DomainMap.Symbols.Members;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.MappingBodyBuilders.BuilderContext;

/// <summary>
/// An implementation of an <see cref="INewInstanceBuilderContext{T}"/>
/// which supports containers (<seealso cref="MembersContainerBuilderContext{T}"/>).
/// </summary>
/// <typeparam name="T"></typeparam>
public class NewInstanceContainerBuilderContext<T>(MappingBuilderContext builderContext, T mapping)
    : MembersContainerBuilderContext<T>(builderContext, mapping),
        INewInstanceBuilderContext<T>
    where T : INewInstanceObjectMemberMapping, IMemberAssignmentTypeMapping
{
    public void AddConstructorParameterMapping(ConstructorParameterMapping mapping)
    {
        Mapping.AddConstructorParameterMapping(mapping);
        MappingAdded(mapping.MemberInfo, true);
    }

    public void AddInitMemberMapping(MemberAssignmentMapping mapping)
    {
        Mapping.AddInitMemberMapping(mapping);
        MappingAdded(mapping.MemberInfo);
    }

    public bool TryMatchInitOnlyMember(IMappableMember targetMember, [NotNullWhen(true)] out MemberMappingInfo? memberInfo)
    {
        if (TryMatchMember(targetMember, out memberInfo))
            return true;

        if (TryGetMemberValueConfigs(targetMember.Name, false, out var valueConfigs))
        {
            BuilderContext.ReportDiagnostic(
                DiagnosticDescriptors.InitOnlyMemberDoesNotSupportPaths,
                Mapping.TargetType,
                valueConfigs[0].Target.FullName
            );
            ConsumeMemberConfig(valueConfigs[0]);
            return false;
        }

        if (TryGetMemberConfigs(targetMember.Name, false, out var configs))
        {
            BuilderContext.ReportDiagnostic(
                DiagnosticDescriptors.InitOnlyMemberDoesNotSupportPaths,
                Mapping.TargetType,
                configs[0].Target.FullName
            );
            ConsumeMemberConfig(configs[0]);
            return false;
        }

        return false;
    }

    public bool TryMatchParameter(IParameterSymbol parameter, [NotNullWhen(true)] out MemberMappingInfo? memberInfo) =>
        TryMatchMember(new ConstructorParameterMember(parameter, BuilderContext.SymbolAccessor), true, out memberInfo);
}
