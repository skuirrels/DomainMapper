using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed class MappingMethodConfiguration
{
    public MappingMethodConfiguration(
        IMethodSymbol method,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        int completeness,
        ImmutableDictionary<string, MemberBinding> bindings,
        ImmutableHashSet<string> ignoredTargets,
        ImmutableHashSet<string> ignoredSources,
        ImmutableHashSet<string>? onlyTargets,
        ImmutableDictionary<string, int> nullBehaviors,
        ImmutableDictionary<string, string> nullSubstitutes,
        ImmutableDictionary<string, IMethodSymbol> computedMembers,
        ImmutableDictionary<string, IMethodSymbol> conditions,
        ImmutableArray<IMethodSymbol> completionHooks,
        int? maximumDepth,
        int depthExhaustionBehavior,
        ImmutableDictionary<string, int> collectionPolicies,
        bool preserveReferences
    )
    {
        Method = method;
        SourceType = sourceType;
        TargetType = targetType;
        Completeness = completeness;
        Bindings = bindings;
        IgnoredTargets = ignoredTargets;
        IgnoredSources = ignoredSources;
        OnlyTargets = onlyTargets;
        NullBehaviors = nullBehaviors;
        NullSubstitutes = nullSubstitutes;
        ComputedMembers = computedMembers;
        Conditions = conditions;
        CompletionHooks = completionHooks;
        MaximumDepth = maximumDepth;
        DepthExhaustionBehavior = depthExhaustionBehavior;
        CollectionPolicies = collectionPolicies;
        PreserveReferences = preserveReferences;
    }

    public IMethodSymbol Method { get; }

    public ITypeSymbol SourceType { get; }

    public ITypeSymbol TargetType { get; }

    public int Completeness { get; }

    public ImmutableDictionary<string, MemberBinding> Bindings { get; }

    public ImmutableHashSet<string> IgnoredTargets { get; }

    public ImmutableHashSet<string> IgnoredSources { get; }

    public ImmutableHashSet<string>? OnlyTargets { get; }

    public ImmutableDictionary<string, int> NullBehaviors { get; }

    public ImmutableDictionary<string, string> NullSubstitutes { get; }

    public ImmutableDictionary<string, IMethodSymbol> ComputedMembers { get; }

    public ImmutableDictionary<string, IMethodSymbol> Conditions { get; }

    public ImmutableArray<IMethodSymbol> CompletionHooks { get; }

    public int? MaximumDepth { get; }

    public int DepthExhaustionBehavior { get; }

    public ImmutableDictionary<string, int> CollectionPolicies { get; }

    public bool PreserveReferences { get; }

    public bool EnforceTarget => Completeness is 0 or 2;

    public bool EnforceSource => Completeness is 1 or 2;
}
