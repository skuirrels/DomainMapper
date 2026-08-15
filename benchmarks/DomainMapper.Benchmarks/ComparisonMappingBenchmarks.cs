using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using DomainMapper.Abstractions;
using MapperlyFactory = Riok.Mapperly.Abstractions.ObjectFactoryAttribute;
using MapperlyMapper = Riok.Mapperly.Abstractions.MapperAttribute;
using MapperlyMapProperty = Riok.Mapperly.Abstractions.MapPropertyAttribute;

namespace DomainMapper.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(BalancedComparisonConfig))]
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

    private readonly BenchmarkFlatTarget _domainMapperExisting = new();
    private readonly BenchmarkFlatTarget _mapperlyExisting = new();
    private readonly BenchmarkIdSource _idSource = new(42);
    private readonly BenchmarkRenamedSource _renamed = new(42, DateTimeOffset.UnixEpoch, new BenchmarkWarehouseSource("London Gateway"));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Flat")]
    public BenchmarkFlatTarget MapperlyFlat() => MapperlyBenchmarkMapper.MapFlat(_flat);

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public BenchmarkFlatTarget DomainMapperFlat() => DomainMapperBenchmarkMapper.MapFlat(_flat);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("RenamedFlattened")]
    public BenchmarkRenamedTarget MapperlyRenamedFlattened() => MapperlyBenchmarkMapper.MapRenamed(_renamed);

    [Benchmark]
    [BenchmarkCategory("RenamedFlattened")]
    public BenchmarkRenamedTarget DomainMapperRenamedFlattened() => DomainMapperBenchmarkMapper.MapRenamed(_renamed);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("NestedCollection")]
    public BenchmarkOrderTarget MapperlyNestedCollection() => MapperlyBenchmarkMapper.MapOrder(_order);

    [Benchmark]
    [BenchmarkCategory("NestedCollection")]
    public BenchmarkOrderTarget DomainMapperNestedCollection() => DomainMapperBenchmarkMapper.MapOrder(_order);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ExistingTarget")]
    public BenchmarkFlatTarget MapperlyExistingTarget()
    {
        MapperlyBenchmarkMapper.UpdateFlat(_flat, _mapperlyExisting);
        return _mapperlyExisting;
    }

    [Benchmark]
    [BenchmarkCategory("ExistingTarget")]
    public BenchmarkFlatTarget DomainMapperExistingTarget()
    {
        DomainMapperBenchmarkMapper.UpdateFlat(_flat, _domainMapperExisting);
        return _domainMapperExisting;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DomainFactory")]
    public BenchmarkAggregate MapperlyDomainFactory() => MapperlyBenchmarkMapper.Place(_flat);

    [Benchmark]
    [BenchmarkCategory("DomainFactory")]
    public BenchmarkAggregate DomainMapperDomainFactory() => DomainMapperBenchmarkMapper.Place(_flat);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ValueObjectFactory")]
    public BenchmarkIdTarget MapperlyValueObjectFactory() => MapperlyBenchmarkMapper.MapId(_idSource);

    [Benchmark]
    [BenchmarkCategory("ValueObjectFactory")]
    public BenchmarkIdTarget DomainMapperValueObjectFactory() => DomainMapperBenchmarkMapper.MapId(_idSource);
}

[DomainMapper]
public static partial class DomainMapperBenchmarkMapper
{
    public static partial BenchmarkFlatTarget MapFlat(BenchmarkFlatSource source);

    [MapMember(nameof(BenchmarkRenamedTarget.EdcId), nameof(BenchmarkRenamedSource.ID))]
    [MapMember(nameof(BenchmarkRenamedTarget.CreatedDate), nameof(BenchmarkRenamedSource.DateCreated))]
    [MapMember(
        nameof(BenchmarkRenamedTarget.TransitWarehouseDescription),
        nameof(BenchmarkRenamedSource.Warehouse) + "." + nameof(BenchmarkWarehouseSource.Description)
    )]
    public static partial BenchmarkRenamedTarget MapRenamed(BenchmarkRenamedSource source);

    public static partial BenchmarkOrderTarget MapOrder(BenchmarkOrderSource source);

    public static partial void UpdateFlat(BenchmarkFlatSource source, BenchmarkFlatTarget target);

    [DomainFactory(Input = DomainFactoryInput.Source)]
    private static BenchmarkAggregateId ToAggregateId(int value) => new BenchmarkAggregateId(value);

    [MapToFactory(nameof(BenchmarkAggregate.Create))]
    public static partial BenchmarkAggregate Place(BenchmarkFlatSource source);

    public static partial BenchmarkIdTarget MapId(BenchmarkIdSource source);
}

#pragma warning disable RMG066 // Mapperly cannot account for members consumed inside a whole-source object factory.
[MapperlyMapper]
public static partial class MapperlyBenchmarkMapper
{
    public static partial BenchmarkFlatTarget MapFlat(BenchmarkFlatSource source);

    [MapperlyMapProperty(nameof(BenchmarkRenamedSource.ID), nameof(BenchmarkRenamedTarget.EdcId))]
    [MapperlyMapProperty(nameof(BenchmarkRenamedSource.DateCreated), nameof(BenchmarkRenamedTarget.CreatedDate))]
    [MapperlyMapProperty(
        nameof(BenchmarkRenamedSource.Warehouse) + "." + nameof(BenchmarkWarehouseSource.Description),
        nameof(BenchmarkRenamedTarget.TransitWarehouseDescription)
    )]
    public static partial BenchmarkRenamedTarget MapRenamed(BenchmarkRenamedSource source);

    public static partial BenchmarkOrderTarget MapOrder(BenchmarkOrderSource source);

    public static partial void UpdateFlat(BenchmarkFlatSource source, BenchmarkFlatTarget target);

    private static BenchmarkAggregateId ToAggregateId(int value) => new BenchmarkAggregateId(value);

    [MapperlyFactory]
    private static BenchmarkAggregate Create(BenchmarkFlatSource source) =>
        BenchmarkAggregate.Create(new BenchmarkAggregateId(source.Id), source.Name, source.Amount, source.CreatedAt);

    public static partial BenchmarkAggregate Place(BenchmarkFlatSource source);

    public static partial BenchmarkIdTarget MapId(BenchmarkIdSource source);
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

public sealed record BenchmarkIdSource(int Id);

public sealed record BenchmarkIdTarget(BenchmarkAggregateId Id);

public sealed record BenchmarkWarehouseSource(string Description);

public sealed record BenchmarkRenamedSource(int ID, DateTimeOffset DateCreated, BenchmarkWarehouseSource Warehouse);

public sealed record BenchmarkRenamedTarget(int EdcId, DateTimeOffset CreatedDate, string TransitWarehouseDescription);

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
