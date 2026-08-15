using DomainMapper.Abstractions;
#if DOMAINMAPPER_PROJECTION_SMOKE
using System.Linq;
using System.Linq.Expressions;
using DomainMapper.Projections;
#endif

var target = PackageSmokeMapper.Map(new PackageSmokeSource { Id = 42, Name = "DomainMapper" });
if (target.ExternalId != 42 || target.Name != "DomainMapper")
    throw new InvalidOperationException("DomainMapper generated an invalid package smoke mapping.");

var graph = new PackageSmokeGraphSource { Value = 7 };
graph.Next = graph;
var mappedGraph = PackageSmokeMapper.MapGraph(graph);
var runtimeGraph = (PackageSmokeGraphTarget)PackageSmokeMapper.MapRuntime(graph, typeof(PackageSmokeGraphTarget))!;
if (!ReferenceEquals(mappedGraph, mappedGraph.Next) || !ReferenceEquals(runtimeGraph, runtimeGraph.Next))
    throw new InvalidOperationException("DomainMapper generated invalid reference-tracking or registry code.");

#if DOMAINMAPPER_PROJECTION_SMOKE
var projection = PackageSmokeMapper.Project();
var projected = new[]
{
    new PackageSmokeSource { Id = 11, Name = "Projection" },
}.AsQueryable().Select(projection).Single();
if (!ReferenceEquals(projection, PackageSmokeMapper.Project()) || projected.ExternalId != 11 || projected.Name != "Projection")
    throw new InvalidOperationException("DomainMapper generated an invalid optional-package projection.");
#endif

Console.WriteLine("DomainMapper package smoke test passed.");

[DomainMapper]
[MapRegistry]
public static partial class PackageSmokeMapper
{
    [MapMember(nameof(PackageSmokeTarget.ExternalId), nameof(PackageSmokeSource.Id))]
    public static partial PackageSmokeTarget Map(PackageSmokeSource source);

    [MapReferenceTracking]
    public static partial PackageSmokeGraphTarget MapGraph(PackageSmokeGraphSource source);

#if DOMAINMAPPER_PROJECTION_SMOKE
    [MapProjection(nameof(Map))]
    public static partial Expression<Func<PackageSmokeSource, PackageSmokeTarget>> Project();
#endif
}

public sealed class PackageSmokeSource
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class PackageSmokeTarget
{
    public int ExternalId { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class PackageSmokeGraphSource
{
    public int Value { get; set; }

    public PackageSmokeGraphSource? Next { get; set; }
}

public sealed class PackageSmokeGraphTarget
{
    public int Value { get; set; }

    public PackageSmokeGraphTarget? Next { get; set; }
}
