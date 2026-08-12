using System.Collections.Immutable;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

namespace DomainMapper.Benchmarks;

internal sealed class BalancedComparisonConfig : ManualConfig
{
    internal const string ArtifactsEnvironmentVariable = "DOMAINMAPPER_BENCHMARK_ARTIFACTS";

    public BalancedComparisonConfig()
    {
        Orderer = new BalancedComparisonOrderer();
        ArtifactsPath = Environment.GetEnvironmentVariable(ArtifactsEnvironmentVariable) ?? "artifacts";
    }
}

internal sealed class SourceGeneratorBenchmarkConfig : ManualConfig
{
    public SourceGeneratorBenchmarkConfig()
    {
        Orderer = new BalancedComparisonOrderer();
        ArtifactsPath = Environment.GetEnvironmentVariable(BalancedComparisonConfig.ArtifactsEnvironmentVariable) ?? "artifacts";
    }
}

internal sealed class BalancedComparisonOrderer : DefaultOrderer
{
    internal const string OrderEnvironmentVariable = "DOMAINMAPPER_BENCHMARK_ORDER";
    internal const string DomainMapperFirst = "domainmapper-first";
    internal const string MapperlyFirst = "mapperly-first";

    private readonly bool _domainMapperFirst;

    public BalancedComparisonOrderer()
        : this(Environment.GetEnvironmentVariable(OrderEnvironmentVariable)) { }

    internal BalancedComparisonOrderer(string? order)
        : base(SummaryOrderPolicy.FastestToSlowest)
    {
        _domainMapperFirst = string.Equals(order, DomainMapperFirst, StringComparison.OrdinalIgnoreCase);
    }

    public override IEnumerable<BenchmarkCase> GetExecutionOrder(
        ImmutableArray<BenchmarkCase> benchmarkCases,
        IEnumerable<BenchmarkLogicalGroupRule>? order = null
    )
    {
        var defaultOrder = base.GetExecutionOrder(benchmarkCases, order).ToArray();
        foreach (var logicalGroup in defaultOrder.GroupBy(x => GetLogicalGroupKey(benchmarkCases, x)))
        {
            foreach (var benchmark in logicalGroup.OrderBy(GetImplementationRank).ThenBy(x => x.DisplayInfo, StringComparer.Ordinal))
            {
                yield return benchmark;
            }
        }
    }

    private int GetImplementationRank(BenchmarkCase benchmark)
    {
        var methodName = benchmark.Descriptor.WorkloadMethod.Name;
        var isDomainMapper = methodName.StartsWith("DomainMapper", StringComparison.Ordinal);
        var isMapperly = methodName.StartsWith("Mapperly", StringComparison.Ordinal);
        if (!isDomainMapper && !isMapperly)
            return 2;

        return isDomainMapper == _domainMapperFirst ? 0 : 1;
    }
}
