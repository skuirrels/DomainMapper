using DomainMap.Benchmarks;

namespace DomainMap.Tests.Benchmarking;

public class ComparisonBenchmarkGateTest
{
    [Fact]
    public void PassesPairedResultWithinTimeAndAllocationLimits()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 120, 96));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 1.10, 32));

        result.Passed.ShouldBeTrue();
        result.Comparisons.ShouldHaveSingleItem().Scenario.ShouldBe("Flat");
    }

    [Fact]
    public void FailsWhenTimeRatioExceedsLimit()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 126, 64));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 1.10, 0));

        result.Passed.ShouldBeFalse();
        result.Comparisons.ShouldHaveSingleItem().Failure.ShouldNotBeNull().ShouldContain("time ratio");
    }

    [Fact]
    public void FailsWhenAllocationExceedsRatioAndSlack()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 100, 136));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 1.10, 64));

        result.Passed.ShouldBeFalse();
        result.Comparisons.ShouldHaveSingleItem().Failure.ShouldNotBeNull().ShouldContain("allocated bytes");
    }

    [Fact]
    public void FailsWhenDomainMapPairIsMissing()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 1.10, 64));

        result.Passed.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldContain("Missing DomainMap benchmark pair");
    }

    [Fact]
    public void FailsWhenMapperlyPairIsMissing()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 100, 64), ("DomainMapUnpaired", 100, 64));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 1.10, 64));

        result.Passed.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldContain("Missing Mapperly benchmark pair");
    }

    private static string WriteReport(params (string Method, double Mean, double AllocatedBytes)[] benchmarks)
    {
        var path = Path.Combine(Path.GetTempPath(), $"domainmap-benchmark-{Guid.NewGuid():N}.json");
        var entries = string.Join(
            ",",
            benchmarks.Select(x =>
                $$"""
                    {
                      "Method": "{{x.Method}}",
                      "Statistics": { "Mean": {{x.Mean}} },
                      "Memory": { "BytesAllocatedPerOperation": {{x.AllocatedBytes}} }
                    }
                    """
            )
        );
        File.WriteAllText(path, $$"""{ "Benchmarks": [{{entries}}] }""");
        return path;
    }
}
