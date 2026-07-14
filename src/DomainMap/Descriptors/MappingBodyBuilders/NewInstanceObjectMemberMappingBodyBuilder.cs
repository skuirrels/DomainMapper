using System.Diagnostics.CodeAnalysis;
using DomainMap.Abstractions;
using DomainMap.Descriptors.Constructors;
using DomainMap.Descriptors.MappingBodyBuilders.BuilderContext;
using DomainMap.Descriptors.Mappings;
using DomainMap.Descriptors.Mappings.MemberMappings;
using DomainMap.Descriptors.ObjectFactories;
using DomainMap.Diagnostics;
using DomainMap.Helpers;
using DomainMap.Symbols.Members;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.MappingBodyBuilders;

/// <summary>
/// Body builder for new instance object member mappings (mappings for which the target object gets created via <code>new()</code>).
/// </summary>
public static class NewInstanceObjectMemberMappingBodyBuilder
{
    public static void BuildMappingBody(MappingBuilderContext ctx, NewInstanceObjectMemberMapping mapping)
    {
        var mappingCtx = new NewInstanceBuilderContext<NewInstanceObjectMemberMapping>(ctx, mapping);
        BuildConstructorMapping(mappingCtx);
        if (mapping.Constructor.SupportsMemberAssignment)
        {
            BuildInitMemberMappings(mappingCtx, true);
        }
        else
        {
            MarkTargetMembersOwnedByDomainFactory(mappingCtx);
        }

        if (mapping.Constructor is not UnimplementedInstanceConstructor)
            mappingCtx.AddDiagnostics(true);
    }

    public static void BuildMappingBody(MappingBuilderContext ctx, NewInstanceObjectMemberMethodMapping mapping)
    {
        var mappingCtx = new NewInstanceContainerBuilderContext<NewInstanceObjectMemberMethodMapping>(ctx, mapping);
        BuildConstructorMapping(mappingCtx);
        if (mapping.Constructor.SupportsMemberAssignment)
        {
            BuildInitMemberMappings(mappingCtx);
            ObjectMemberMappingBodyBuilder.BuildMappingBody(mappingCtx);
        }
        else
        {
            MarkTargetMembersOwnedByDomainFactory(mappingCtx);
        }

        if (mapping.Constructor is not UnimplementedInstanceConstructor)
            mappingCtx.AddDiagnostics(true);
    }

    public static IReadOnlyList<ConstructorParameterMapping> BuildConstructorMapping(
        INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx,
        bool? preferParameterlessConstructor = null
    )
    {
        if (ctx.Mapping.HasConstructor)
        {
            return TryBuildConstructorMapping(ctx, ctx.Mapping.Constructor, out var parameterMappings, out _) ? parameterMappings : [];
        }

        if (TryBuildObjectFactoryMapping(ctx, out var factoryParameterMappings))
            return factoryParameterMappings;

        if (ctx.Mapping.TargetType is not INamedTypeSymbol namedTargetType)
        {
            ctx.BuilderContext.ReportDiagnostic(DiagnosticDescriptors.NoConstructorFound, ctx.BuilderContext.Target);
            return [];
        }

        // attributed ctor is prio 1
        // if preferParameterlessConstructors is true (default) :parameterless ctor is prio 2 then by descending parameter count
        // the reverse if preferParameterlessConstructors is false , descending parameter count is prio2 then parameterless ctor
        // ctors annotated with [Obsolete] are considered last unless they have a MapperConstructor attribute set
        var ctorCandidates = namedTargetType
            .InstanceConstructors.Where(ctor => ctx.BuilderContext.SymbolAccessor.IsConstructorAccessible(ctor))
            .OrderByDescending(x => ctx.BuilderContext.SymbolAccessor.HasAttribute<MapperConstructorAttribute>(x))
            .ThenBy(x => ctx.BuilderContext.SymbolAccessor.HasAttribute<ObsoleteAttribute>(x));

        if (preferParameterlessConstructor ?? ctx.BuilderContext.Configuration.Mapper.PreferParameterlessConstructors)
        {
            ctorCandidates = ctorCandidates.ThenByDescending(x => x.Parameters.Length == 0).ThenByDescending(x => x.Parameters.Length);
        }
        else
        {
            ctorCandidates = ctorCandidates.ThenByDescending(x => x.Parameters.Length).ThenByDescending(x => x.Parameters.Length == 0);
        }

        foreach (var ctorCandidate in ctorCandidates)
        {
            if (!TryBuildConstructorMapping(ctx, ctorCandidate, out var constructorParameterMappings, out _))
            {
                if (ctx.BuilderContext.SymbolAccessor.HasAttribute<MapperConstructorAttribute>(ctorCandidate))
                {
                    ctx.BuilderContext.ReportDiagnostic(
                        DiagnosticDescriptors.CannotMapToConfiguredConstructor,
                        ctx.Mapping.SourceType,
                        ctorCandidate
                    );
                }

                continue;
            }

            ctx.Mapping.Constructor = ctx.BuilderContext.InstanceConstructors.BuildForConstructor(ctorCandidate);

            foreach (var mapping in constructorParameterMappings)
            {
                ctx.AddConstructorParameterMapping(mapping);
            }

            return constructorParameterMappings;
        }

        ctx.BuilderContext.ReportDiagnostic(DiagnosticDescriptors.NoConstructorFound, ctx.BuilderContext.Target);
        ctx.Mapping.Constructor = new InstanceConstructor(namedTargetType);
        return [];
    }

    private static bool TryBuildObjectFactoryMapping(
        INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx,
        out IReadOnlyList<ConstructorParameterMapping> parameterMappings
    )
    {
        parameterMappings = [];
        if (TryBuildConfiguredDomainFactoryMapping(ctx, out parameterMappings))
            return true;

        var objectFactoryConstructors = ctx
            .BuilderContext.InstanceConstructors.BuildForObjectFactories(ctx.Mapping.SourceType, ctx.Mapping.TargetType)
            .ToArray();
        var domainFactoryConstructors = objectFactoryConstructors
            .OfType<ObjectFactoryConstructorAdapter>()
            .Where(x => x.IsDomainFactory)
            .ToArray();
        if (ctx.BuilderContext.IsExpression && domainFactoryConstructors.Length > 0)
        {
            ReportProjectionDomainFactory(ctx, domainFactoryConstructors[0]);
            return true;
        }

        IEnumerable<IInstanceConstructor> factoryConstructors =
            domainFactoryConstructors.Length == 0 ? objectFactoryConstructors : domainFactoryConstructors;
        var unsatisfiedDomainFactories = new List<(ObjectFactoryConstructorAdapter Factory, IReadOnlyList<string> Parameters)>();

        foreach (var objectFactoryConstructor in factoryConstructors)
        {
            if (ShouldSkipObjectFactoryConstructor(ctx, objectFactoryConstructor))
            {
                continue;
            }

            if (
                TryBuildConstructorMapping(ctx, objectFactoryConstructor, out var candidateParameterMappings, out var unsatisfiedParameters)
            )
            {
                ctx.Mapping.Constructor = objectFactoryConstructor;
                parameterMappings = candidateParameterMappings;
                return true;
            }

            if (objectFactoryConstructor is ObjectFactoryConstructorAdapter { IsDomainFactory: true } unsatisfiedFactory)
                unsatisfiedDomainFactories.Add((unsatisfiedFactory, unsatisfiedParameters));
        }

        if (unsatisfiedDomainFactories.Count == 0)
            return false;

        foreach (var (factory, parameters) in unsatisfiedDomainFactories)
        {
            ctx.BuilderContext.ReportDiagnosticAtSymbol(
                DiagnosticDescriptors.DomainFactoryCannotBeSatisfied,
                factory.ParameterMappingMethod,
                factory.ParameterMappingMethod.Name,
                ctx.Mapping.TargetType,
                ctx.Mapping.SourceType,
                string.Join(", ", parameters)
            );
        }

        ctx.Mapping.Constructor = new UnimplementedInstanceConstructor(ctx.Mapping.TargetType);
        return true;
    }

    private static bool TryBuildConfiguredDomainFactoryMapping(
        INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx,
        out IReadOnlyList<ConstructorParameterMapping> parameterMappings
    )
    {
        parameterMappings = [];
        var mappingMethod = ctx.BuilderContext.UserSymbol;
        if (mappingMethod == null || ctx.BuilderContext.Configuration.TargetFactoryMethodName is not { } factoryName)
            return false;
        var targetType = ctx.Mapping.TargetType.NonNullable();
        var factoryMethods = string.IsNullOrWhiteSpace(factoryName)
            ? []
            : ctx
                .BuilderContext.SymbolAccessor.GetAllMethods(targetType)
                .Where(method => IsValidConfiguredDomainFactory(ctx.BuilderContext, method, targetType, factoryName))
                .ToArray();

        if (factoryMethods.Length == 0)
        {
            ctx.BuilderContext.ReportDiagnosticAtSymbol(
                DiagnosticDescriptors.ConfiguredDomainFactoryNotFound,
                mappingMethod,
                targetType,
                string.IsNullOrWhiteSpace(factoryName) ? "<empty>" : factoryName
            );
            ctx.Mapping.Constructor = new UnimplementedInstanceConstructor(ctx.Mapping.TargetType);
            return true;
        }

        if (ctx.BuilderContext.IsExpression)
        {
            ctx.BuilderContext.ReportDiagnosticAtSymbol(
                DiagnosticDescriptors.DomainFactoryCannotBeUsedInProjection,
                mappingMethod,
                factoryName,
                targetType
            );
            ctx.Mapping.Constructor = new UnimplementedInstanceConstructor(ctx.Mapping.TargetType);
            return true;
        }

        var unsatisfiedFactories = new List<(IMethodSymbol Factory, IReadOnlyList<string> Parameters)>();
        foreach (var factoryMethod in factoryMethods)
        {
            var factory = new TargetStaticParameterObjectFactory(ctx.BuilderContext.SymbolAccessor, factoryMethod);
            var constructor = ctx.BuilderContext.InstanceConstructors.BuildForObjectFactory(
                factory,
                ctx.Mapping.SourceType,
                ctx.Mapping.TargetType
            );
            if (TryBuildConstructorMapping(ctx, constructor, out var candidateParameterMappings, out var unsatisfiedParameters))
            {
                ctx.Mapping.Constructor = constructor;
                parameterMappings = candidateParameterMappings;
                return true;
            }

            unsatisfiedFactories.Add((factoryMethod, unsatisfiedParameters));
        }

        foreach (var (factory, parameters) in unsatisfiedFactories)
        {
            ctx.BuilderContext.ReportDiagnosticAtSymbol(
                DiagnosticDescriptors.DomainFactoryCannotBeSatisfied,
                mappingMethod,
                factory.Name,
                targetType,
                ctx.Mapping.SourceType,
                string.Join(", ", parameters)
            );
        }

        ctx.Mapping.Constructor = new UnimplementedInstanceConstructor(ctx.Mapping.TargetType);
        return true;
    }

    private static bool IsValidConfiguredDomainFactory(
        MappingBuilderContext ctx,
        IMethodSymbol method,
        ITypeSymbol targetType,
        string factoryName
    )
    {
        return string.Equals(method.Name, factoryName, StringComparison.Ordinal)
            && method.MethodKind == MethodKind.Ordinary
            && method.IsStatic
            && !method.IsAsync
            && !method.IsGenericMethod
            && !method.IsPartialDefinition
            && !method.ReturnsVoid
            && !method.ReturnsByRef
            && !method.ReturnsByRefReadonly
            && method.Parameters.All(parameter => parameter.RefKind is RefKind.None or RefKind.In)
            && SymbolEqualityComparer.Default.Equals(method.ReturnType, targetType)
            && ctx.SymbolAccessor.IsDirectlyAccessible(method);
    }

    private static void ReportProjectionDomainFactory(
        INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx,
        ObjectFactoryConstructorAdapter domainFactory
    )
    {
        ctx.BuilderContext.ReportDiagnosticAtSymbol(
            DiagnosticDescriptors.DomainFactoryCannotBeUsedInProjection,
            domainFactory.ParameterMappingMethod,
            domainFactory.ParameterMappingMethod.Name,
            ctx.Mapping.TargetType
        );
        ctx.Mapping.Constructor = new UnimplementedInstanceConstructor(ctx.Mapping.TargetType);
    }

    private static void MarkTargetMembersOwnedByDomainFactory(INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx)
    {
        if (ctx.Mapping.Constructor is ObjectFactoryConstructorAdapter { IsDomainFactory: true, SupportsParameterMapping: false })
        {
            ctx.SetAllSourceMembersMapped();
        }

        foreach (var targetMember in ctx.EnumerateUnmappedTargetMembers().ToArray())
        {
            ctx.SetTargetMemberMapped(targetMember);
        }
    }

    private static bool ShouldSkipObjectFactoryConstructor(
        INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx,
        IInstanceConstructor constructor
    ) => ctx.BuilderContext.IsExpression && constructor is IParameterMappingInstanceConstructor { SupportsParameterMapping: true };

    private static bool TryBuildConstructorMapping(
        INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx,
        IInstanceConstructor constructor,
        [NotNullWhen(true)] out IReadOnlyList<ConstructorParameterMapping>? constructorParameterMappings,
        out IReadOnlyList<string> unsatisfiedParameters
    )
    {
        constructorParameterMappings = [];
        unsatisfiedParameters = [];

        if (constructor is not IParameterMappingInstanceConstructor { SupportsParameterMapping: true } parameterMappingConstructor)
        {
            return true;
        }

        if (
            !TryBuildConstructorMapping(
                ctx,
                parameterMappingConstructor.ParameterMappingMethod,
                out var parameterMappings,
                out unsatisfiedParameters
            )
        )
        {
            return false;
        }

        foreach (var mapping in parameterMappings)
        {
            ctx.AddConstructorParameterMapping(mapping);
        }

        constructorParameterMappings = parameterMappings;
        return true;
    }

    public static void BuildInitMemberMappings(
        INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx,
        bool includeAllMembers = false
    )
    {
        if (!ctx.Mapping.Constructor.SupportsObjectInitializer)
            return;

        var initOnlyTargetMembers = includeAllMembers
            ? ctx.EnumerateUnmappedTargetMembers().Where(x => x.CanSet).ToArray()
            : ctx.EnumerateUnmappedTargetMembers().Where(x => x.CanOnlySetViaInitializer()).ToArray();
        foreach (var targetMember in initOnlyTargetMembers)
        {
            if (ctx.TryMatchInitOnlyMember(targetMember, out var memberInfo))
            {
                BuildInitMemberMapping(ctx, memberInfo);
                continue;
            }

            // set the member mapped as it is an init only member
            // diagnostics are already reported
            // and no further mapping attempts should be undertaken
            if (
                targetMember.IsRequired
                || ctx.BuilderContext.Configuration.HasRequiredMappingStrategyForMembers(RequiredMappingStrategy.Target)
            )
            {
                ctx.BuilderContext.ReportDiagnostic(
                    targetMember.IsRequired ? DiagnosticDescriptors.RequiredMemberNotMapped : DiagnosticDescriptors.SourceMemberNotFound,
                    targetMember.Name,
                    ctx.Mapping.TargetType,
                    ctx.Mapping.SourceType
                );
            }
            ctx.SetTargetMemberMapped(targetMember);
        }
    }

    private static void BuildInitMemberMapping(
        INewInstanceBuilderContext<INewInstanceObjectMemberMapping> ctx,
        MemberMappingInfo memberInfo
    )
    {
        // consume member configs
        // to ensure no further mappings are created for these configurations,
        // even if a mapping validation fails
        ctx.ConsumeMemberConfigs(memberInfo);

        if (!ObjectMemberMappingBodyBuilder.ValidateMappingSpecification(ctx, memberInfo, true))
            return;

        if (!MemberMappingBuilder.TryBuildAssignment(ctx, memberInfo, out var memberAssignmentMapping))
            return;

        ctx.AddInitMemberMapping(memberAssignmentMapping);
    }

    private static bool TryBuildConstructorMapping(
        INewInstanceBuilderContext<IMapping> ctx,
        IMethodSymbol ctor,
        [NotNullWhen(true)] out List<ConstructorParameterMapping>? constructorParameterMappings,
        out IReadOnlyList<string> unsatisfiedParameters
    )
    {
        constructorParameterMappings = [];
        var unsatisfiedParameterNames = new List<string>();

        var skippedOptionalParam = false;
        foreach (var parameter in ctor.Parameters)
        {
            if (
                !ctx.TryMatchParameter(parameter, out var memberMappingInfo)
                || !SourceValueBuilder.TryBuildMappedSourceValue(ctx, memberMappingInfo, out var sourceValue)
            )
            {
                // expressions do not allow skipping of optional parameters
                if (!parameter.IsOptional || ctx.BuilderContext.IsExpression)
                {
                    unsatisfiedParameterNames.Add(parameter.Name);
                    unsatisfiedParameters = unsatisfiedParameterNames;
                    return false;
                }

                skippedOptionalParam = true;
                continue;
            }

            var ctorMapping = new ConstructorParameterMapping(parameter, sourceValue, skippedOptionalParam, memberMappingInfo);
            constructorParameterMappings.Add(ctorMapping);
        }

        unsatisfiedParameters = unsatisfiedParameterNames;
        return true;
    }
}
