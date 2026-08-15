using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace DomainMapper.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(BalancedComparisonConfig))]
public class AdvancedFeatureBenchmarks
{
    private static readonly Expression<Func<BenchmarkRenamedSource, BenchmarkRenamedTarget>> HandWrittenProjection =
        source => new BenchmarkRenamedTarget(source.ID, source.DateCreated, source.Warehouse.Description);

    private readonly BenchmarkFlatSource _flat = new()
    {
        Id = 42,
        Name = "Ada",
        Amount = 12.5m,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };
    private readonly BenchmarkCollectionSource _collection = new([1, 2, 3, 4, 5, 6, 7, 8]);
    private readonly BenchmarkCollectionTarget _domainCollection = new();
    private readonly BenchmarkCollectionTarget _handCollection = new();
    private readonly BenchmarkGraphSource _graph;

    public AdvancedFeatureBenchmarks()
    {
        _graph = new BenchmarkGraphSource { Value = 42 };
        _graph.Next = _graph;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("RegistryDispatch")]
    public object HandWrittenRegistry() => HandWrittenMap(_flat, typeof(BenchmarkFlatTarget));

    [Benchmark]
    [BenchmarkCategory("RegistryDispatch")]
    public object DomainMapperRegistry() => DomainMapperBenchmarkMapper.MapRuntime(_flat, typeof(BenchmarkFlatTarget))!;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ReferenceTracking")]
    public BenchmarkGraphTarget HandWrittenReferenceTracking() => HandWrittenMapGraph(_graph);

    [Benchmark]
    [BenchmarkCategory("ReferenceTracking")]
    public BenchmarkGraphTarget DomainMapperReferenceTracking() => DomainMapperBenchmarkMapper.MapGraph(_graph);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("CollectionClearAndFill")]
    public BenchmarkCollectionTarget HandWrittenCollectionMutation()
    {
        _handCollection.Items.Clear();
        foreach (var item in _collection.Items)
            _handCollection.Items.Add(item);
        return _handCollection;
    }

    [Benchmark]
    [BenchmarkCategory("CollectionClearAndFill")]
    public BenchmarkCollectionTarget DomainMapperCollectionMutation()
    {
        DomainMapperBenchmarkMapper.UpdateCollection(_collection, _domainCollection);
        return _domainCollection;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ProjectionRetrieval")]
    public Expression<Func<BenchmarkRenamedSource, BenchmarkRenamedTarget>> HandWrittenProjectionRetrieval() => HandWrittenProjection;

    [Benchmark]
    [BenchmarkCategory("ProjectionRetrieval")]
    public Expression<Func<BenchmarkRenamedSource, BenchmarkRenamedTarget>> DomainMapperProjectionRetrieval() =>
        DomainMapperBenchmarkMapper.ProjectRenamed();

    private static object HandWrittenMap(object source, Type targetType)
    {
        if (source.GetType() == typeof(BenchmarkFlatSource) && targetType == typeof(BenchmarkFlatTarget))
        {
            var typed = (BenchmarkFlatSource)source;
            return new BenchmarkFlatTarget
            {
                Id = typed.Id,
                Name = typed.Name,
                Amount = typed.Amount,
                CreatedAt = typed.CreatedAt,
            };
        }
        throw new InvalidOperationException();
    }

    private static BenchmarkGraphTarget HandWrittenMapGraph(BenchmarkGraphSource source)
    {
        var references = new Dictionary<ReferenceKey, object>();
        return MapNode(source, references);
    }

    private static BenchmarkGraphTarget MapNode(BenchmarkGraphSource source, Dictionary<ReferenceKey, object> references)
    {
        var referenceKey = new ReferenceKey(source, typeof(BenchmarkGraphTarget));
        if (references.TryGetValue(referenceKey, out var existing))
            return (BenchmarkGraphTarget)existing;
        var target = new BenchmarkGraphTarget { Value = source.Value };
        references.Add(referenceKey, target);
        target.Next = source.Next is null ? null : MapNode(source.Next, references);
        return target;
    }

    private readonly struct ReferenceKey : IEquatable<ReferenceKey>
    {
        private readonly object _source;
        private readonly Type _targetType;

        public ReferenceKey(object source, Type targetType)
        {
            _source = source;
            _targetType = targetType;
        }

        public bool Equals(ReferenceKey other) => ReferenceEquals(_source, other._source) && _targetType == other._targetType;

        public override bool Equals(object? value) => value is ReferenceKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_source) * 397) ^ _targetType.GetHashCode();
            }
        }
    }
}
