using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using DomainMap.Abstractions;
using MapperlyFactory = Riok.Mapperly.Abstractions.ObjectFactoryAttribute;
using MapperlyMapper = Riok.Mapperly.Abstractions.MapperAttribute;

namespace DomainMap.Benchmarks;

[ArtifactsPath("artifacts")]
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[InProcess]
public class ComparisonMappingBenchmarks
{
    private readonly BenchmarkFlatSource _flat = new()
    {
        Id = 42,
        Name = "Ada Lovelace",
        Amount = 123.45m,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private readonly BenchmarkOrderSource _order = new(
        Guid.Parse("53d47944-8e54-4457-b55e-4051d4058d82"),
        new BenchmarkCustomerSource("Ada Lovelace", new BenchmarkAddressSource("12 St James's Square", "London", "SW1Y 4LB")),
        [new BenchmarkLineSource("DOMAIN-001", 2, 42.50m), new BenchmarkLineSource("MAP-002", 1, 12m)]
    );

    private readonly BenchmarkFlatTarget _domainMapExisting = new();
    private readonly BenchmarkFlatTarget _mapperlyExisting = new();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Flat")]
    public BenchmarkFlatTarget MapperlyFlat() => MapperlyBenchmarkMapper.MapFlat(_flat);

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public BenchmarkFlatTarget DomainMapFlat() => DomainMapBenchmarkMapper.MapFlat(_flat);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("NestedCollection")]
    public BenchmarkOrderTarget MapperlyNestedCollection() => MapperlyBenchmarkMapper.MapOrder(_order);

    [Benchmark]
    [BenchmarkCategory("NestedCollection")]
    public BenchmarkOrderTarget DomainMapNestedCollection() => DomainMapBenchmarkMapper.MapOrder(_order);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ExistingTarget")]
    public void MapperlyExistingTarget() => MapperlyBenchmarkMapper.UpdateFlat(_flat, _mapperlyExisting);

    [Benchmark]
    [BenchmarkCategory("ExistingTarget")]
    public void DomainMapExistingTarget() => DomainMapBenchmarkMapper.UpdateFlat(_flat, _domainMapExisting);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DomainFactory")]
    public BenchmarkAggregate MapperlyDomainFactory() => MapperlyBenchmarkMapper.Place(_flat);

    [Benchmark]
    [BenchmarkCategory("DomainFactory")]
    public BenchmarkAggregate DomainMapDomainFactory() => DomainMapBenchmarkMapper.Place(_flat);
}

[DomainMapper]
public static partial class DomainMapBenchmarkMapper
{
    public static partial BenchmarkFlatTarget MapFlat(BenchmarkFlatSource source);

    public static partial BenchmarkOrderTarget MapOrder(BenchmarkOrderSource source);

    public static partial void UpdateFlat(BenchmarkFlatSource source, BenchmarkFlatTarget target);

    private static BenchmarkAggregateId ToAggregateId(int value) => new(value);

    [DomainFactory]
    private static BenchmarkAggregate Create(BenchmarkAggregateId id, string name, decimal amount, DateTimeOffset createdAt) =>
        BenchmarkAggregate.Create(id, name, amount, createdAt);

    public static partial BenchmarkAggregate Place(BenchmarkFlatSource source);
}

#pragma warning disable RMG066 // Mapperly cannot account for members consumed inside a whole-source object factory.
[MapperlyMapper]
public static partial class MapperlyBenchmarkMapper
{
    public static partial BenchmarkFlatTarget MapFlat(BenchmarkFlatSource source);

    public static partial BenchmarkOrderTarget MapOrder(BenchmarkOrderSource source);

    public static partial void UpdateFlat(BenchmarkFlatSource source, BenchmarkFlatTarget target);

    [MapperlyFactory]
    private static BenchmarkAggregate Create(BenchmarkFlatSource source) =>
        BenchmarkAggregate.Create(new BenchmarkAggregateId(source.Id), source.Name, source.Amount, source.CreatedAt);

    public static partial BenchmarkAggregate Place(BenchmarkFlatSource source);
}
#pragma warning restore RMG066

public sealed class BenchmarkFlatSource
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public decimal Amount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class BenchmarkFlatTarget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record BenchmarkAddressSource(string Line1, string City, string PostalCode);

public sealed record BenchmarkAddressTarget(string Line1, string City, string PostalCode);

public sealed record BenchmarkCustomerSource(string Name, BenchmarkAddressSource Address);

public sealed record BenchmarkCustomerTarget(string Name, BenchmarkAddressTarget Address);

public sealed record BenchmarkLineSource(string Sku, int Quantity, decimal UnitPrice);

public sealed record BenchmarkLineTarget(string Sku, int Quantity, decimal UnitPrice);

public sealed record BenchmarkOrderSource(Guid Id, BenchmarkCustomerSource Customer, List<BenchmarkLineSource> Lines);

public sealed record BenchmarkOrderTarget(Guid Id, BenchmarkCustomerTarget Customer, List<BenchmarkLineTarget> Lines);

public readonly record struct BenchmarkAggregateId(int Value);

public sealed class BenchmarkAggregate
{
    private BenchmarkAggregate(BenchmarkAggregateId id, string name, decimal amount, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public BenchmarkAggregateId Id { get; }

    public string Name { get; }

    public decimal Amount { get; }

    public DateTimeOffset CreatedAt { get; }

    public static BenchmarkAggregate Create(BenchmarkAggregateId id, string name, decimal amount, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return new BenchmarkAggregate(id, name, amount, createdAt);
    }
}
