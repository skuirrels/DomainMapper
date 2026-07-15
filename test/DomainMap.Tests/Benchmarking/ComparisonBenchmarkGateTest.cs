using System.Text.Json;
using DomainMap.Benchmarks;

namespace DomainMap.Tests.Benchmarking;

public class ComparisonBenchmarkGateTest
{
    [Fact]
    public void PassesPairedResultWithinTimeAndAllocationLimits()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 120, 96));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 0, 1.10, 32));

        result.Passed.ShouldBeTrue();
        result.Comparisons.ShouldHaveSingleItem().Scenario.ShouldBe("Flat");
    }

    [Fact]
    public void FailsWhenTimeRatioExceedsLimit()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 126, 64));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 0, 1.10, 0));

        result.Passed.ShouldBeFalse();
        result.Comparisons.ShouldHaveSingleItem().Failure.ShouldNotBeNull().ShouldContain("time ratio");
    }

    [Fact]
    public void PassesSubNanosecondDifferenceWithinTimeSlack()
    {
        var report = WriteReport(("MapperlyExistingTarget", 2, 0), ("DomainMapExistingTarget", 2.9, 0));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 0.5, 1.10, 0));

        result.Passed.ShouldBeTrue();
    }

    [Fact]
    public void FailsWhenTimeExceedsRatioAndSlack()
    {
        var report = WriteReport(("MapperlyExistingTarget", 2, 0), ("DomainMapExistingTarget", 3.1, 0));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 0.5, 1.10, 0));

        result.Passed.ShouldBeFalse();
        result.Comparisons.ShouldHaveSingleItem().Failure.ShouldNotBeNull().ShouldContain("median time");
    }

    [Fact]
    public void FailsWhenAllocationExceedsRatioAndSlack()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 100, 136));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 0, 1.10, 64));

        result.Passed.ShouldBeFalse();
        result.Comparisons.ShouldHaveSingleItem().Failure.ShouldNotBeNull().ShouldContain("allocated bytes");
    }

    [Fact]
    public void FailsWhenDomainMapPairIsMissing()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 0, 1.10, 64));

        result.Passed.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldContain("Missing DomainMap benchmark pair");
    }

    [Fact]
    public void FailsWhenMapperlyPairIsMissing()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 100, 64), ("DomainMapUnpaired", 100, 64));

        var result = ComparisonBenchmarkGate.Evaluate(report, new ComparisonGateOptions(1.25, 0, 1.10, 64));

        result.Passed.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldContain("Missing Mapperly benchmark pair");
    }

    [Fact]
    public void AggregatesRawSamplesAcrossBalancedReports()
    {
        var mapperlyFirst = WriteReportWithValues(("MapperlyFlat", [10d, 11], 64), ("DomainMapFlat", [12d, 13], 64));
        var domainMapFirst = WriteReportWithValues(("MapperlyFlat", [12d, 13], 64), ("DomainMapFlat", [10d, 11], 64));
        var options = new ComparisonGateOptions(1, 0, 1, 0)
        {
            ProvenParityScenarios = new HashSet<string>(["Flat"], StringComparer.Ordinal),
        };

        var result = ComparisonBenchmarkGate.Evaluate([mapperlyFirst, domainMapFirst], options);

        var comparison = result.Comparisons.ShouldHaveSingleItem();
        comparison.Passed.ShouldBeTrue();
        comparison.ReportCount.ShouldBe(2);
        comparison.MapperlySampleCount.ShouldBe(4);
        comparison.DomainMapSampleCount.ShouldBe(4);
        comparison.Expectation.ShouldBe("PROVEN PARITY");
        comparison.MapperlyMedianNanoseconds.ShouldBe(11.5);
        comparison.DomainMapMedianNanoseconds.ShouldBe(11.5);
    }

    [Fact]
    public void RequiresEveryBalancedReportToContainEveryScenarioPair()
    {
        var completeReport = WriteReport(("MapperlyFlat", 10, 64), ("DomainMapFlat", 9, 64));
        var emptyReport = WriteReport();

        var result = ComparisonBenchmarkGate.Evaluate([completeReport, emptyReport], new ComparisonGateOptions(1.25, 0, 1, 0));

        result.Passed.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldContain("does not contain a complete pair for Flat");
    }

    [Fact]
    public void ProvenCodeParityIgnoresRawTimingWinnerButNotAllocationRegression()
    {
        var report = WriteReport(("MapperlyFlat", 100, 64), ("DomainMapFlat", 200, 65));
        var options = new ComparisonGateOptions(1, 0, 1, 0)
        {
            ProvenParityScenarios = new HashSet<string>(["Flat"], StringComparer.Ordinal),
        };

        var result = ComparisonBenchmarkGate.Evaluate(report, options);

        result.Passed.ShouldBeFalse();
        result.Comparisons.ShouldHaveSingleItem().Failure.ShouldNotBeNull().ShouldContain("allocated bytes");
    }

    [Fact]
    public void FasterExpectationRequiresOneSidedStatisticalWin()
    {
        var report = WriteReportWithValues(("MapperlyNestedCollection", [10d, 10, 10], 100), ("DomainMapNestedCollection", [8d, 8, 8], 80));
        var options = new ComparisonGateOptions(1.25, 0, 1, 0)
        {
            RequireFasterScenarios = new HashSet<string>(["NestedCollection"], StringComparer.Ordinal),
        };

        var result = ComparisonBenchmarkGate.Evaluate(report, options);

        var comparison = result.Comparisons.ShouldHaveSingleItem();
        comparison.Passed.ShouldBeTrue();
        comparison.Expectation.ShouldBe("FASTER");
        comparison.UpperDifferenceConfidenceBoundNanoseconds.ShouldBeLessThan(0);
    }

    [Fact]
    public void FasterExpectationRejectsInconclusiveSamples()
    {
        var report = WriteReportWithValues(
            ("MapperlyNestedCollection", [10d, 10, 10], 100),
            ("DomainMapNestedCollection", [9d, 10, 11], 100)
        );
        var options = new ComparisonGateOptions(1.25, 0, 1, 0)
        {
            RequireFasterScenarios = new HashSet<string>(["NestedCollection"], StringComparer.Ordinal),
        };

        var result = ComparisonBenchmarkGate.Evaluate(report, options);

        result.Passed.ShouldBeFalse();
        result.Comparisons.ShouldHaveSingleItem().Failure.ShouldNotBeNull().ShouldContain("not statistically faster");
    }

    [Fact]
    public void WildcardRequiresEveryDifferentiatedScenarioToBeFaster()
    {
        var report = WriteReportWithValues(
            ("MapperlyFlat", [10d, 10, 10], 64),
            ("DomainMapFlat", [8d, 8, 8], 64),
            ("MapperlyNestedCollection", [20d, 20, 20], 128),
            ("DomainMapNestedCollection", [21d, 21, 21], 128)
        );
        var options = new ComparisonGateOptions(1.25, 0, 1, 0)
        {
            ProvenParityScenarios = new HashSet<string>(["Flat"], StringComparer.Ordinal),
            RequireFasterScenarios = new HashSet<string>(["*"], StringComparer.Ordinal),
        };

        var result = ComparisonBenchmarkGate.Evaluate(report, options);

        result.Passed.ShouldBeFalse();
        result.Comparisons.Single(x => x.Scenario == "Flat").Expectation.ShouldBe("PROVEN PARITY");
        result.Comparisons.Single(x => x.Scenario == "NestedCollection").Expectation.ShouldBe("FASTER");
    }

    [Fact]
    public void RejectsAStatisticalDecisionWithoutTheConfiguredEvidenceFloor()
    {
        var report = WriteReportWithValues(("MapperlyFlat", [10d, 10, 10], 64), ("DomainMapFlat", [8d, 8, 8], 64));
        var options = new ComparisonGateOptions(1.25, 0, 1, 0)
        {
            RequireFasterScenarios = new HashSet<string>(["*"], StringComparer.Ordinal),
            MinimumReportCount = 2,
            MinimumSampleCount = 4,
        };

        var result = ComparisonBenchmarkGate.Evaluate(report, options);

        result.Passed.ShouldBeFalse();
        var failure = result.Comparisons.ShouldHaveSingleItem().Failure.ShouldNotBeNull();
        failure.ShouldContain("at least 2");
        failure.ShouldContain("at least 4 per implementation");
    }

    [Fact]
    public void GeneratedCodeParityInlinesEquivalentFactoryWrappersAndDetectsCollectionDifference()
    {
        var domainMapGenerated = BuildGeneratedMapper("DomainMapBenchmarkMapper", "MapLine(source)");
        var mapperlyGenerated = BuildGeneratedMapper("MapperlyBenchmarkMapper", "MapLine(source + 1)", placeExpression: "Create(source)");
        var declarations = """
            partial class DomainMapBenchmarkMapper
            {
                private static int ToId(int value) => value;
                private static int Factory(int value) => value;
            }

            partial class MapperlyBenchmarkMapper
            {
                private static int ToId(int value) => value;
                private static int Factory(int value) => value;
                private static int Create(int source) => Factory(ToId(source));
            }
            """;

        var result = ComparisonCodeParity.Evaluate(domainMapGenerated, mapperlyGenerated, declarations);

        result.Scenarios.Single(x => x.Scenario == "DomainFactory").Equivalent.ShouldBeTrue();
        result.Scenarios.Single(x => x.Scenario == "NestedCollection").Equivalent.ShouldBeFalse();
        result.Scenarios.Where(x => x.Scenario != "NestedCollection").ShouldAllBe(x => x.Equivalent);
    }

    [Fact]
    public void GeneratedCodeParityPreservesPerformanceAffectingAttributes()
    {
        var domainMapGenerated = BuildGeneratedMapper("DomainMapBenchmarkMapper", "MapLine(source)")
            .Replace(
                "public static int MapFlat(int source)",
                "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] public static int MapFlat(int source)",
                StringComparison.Ordinal
            );
        var mapperlyGenerated = BuildGeneratedMapper("MapperlyBenchmarkMapper", "MapLine(source)");
        var declarations = """
            partial class DomainMapBenchmarkMapper
            {
                private static int ToId(int value) => value;
                private static int Factory(int value) => value;
            }

            partial class MapperlyBenchmarkMapper
            {
                private static int ToId(int value) => value;
                private static int Factory(int value) => value;
            }
            """;

        var result = ComparisonCodeParity.Evaluate(domainMapGenerated, mapperlyGenerated, declarations);

        result.Scenarios.Single(x => x.Scenario == "Flat").Equivalent.ShouldBeFalse();
        result.Scenarios.Where(x => x.Scenario != "Flat").ShouldAllBe(x => x.Equivalent);
    }

    [Fact]
    public void GeneratedCodeParityIncludesAttributesFromPartialMethodDeclarations()
    {
        var domainMapGenerated = BuildGeneratedMapper("DomainMapBenchmarkMapper", "MapLine(source)")
            .Replace(
                "public static int MapId(int source)",
                "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] public static int MapId(int source)",
                StringComparison.Ordinal
            );
        var mapperlyGenerated = BuildGeneratedMapper("MapperlyBenchmarkMapper", "MapLine(source)");
        var declarations = """
            partial class DomainMapBenchmarkMapper
            {
                private static int ToId(int value) => value;
                private static int Factory(int value) => value;
            }

            partial class MapperlyBenchmarkMapper
            {
                private static int ToId(int value) => value;
                private static int Factory(int value) => value;

                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                public static partial int MapId(int source);
            }
            """;

        var result = ComparisonCodeParity.Evaluate(domainMapGenerated, mapperlyGenerated, declarations);

        result.Scenarios.Single(x => x.Scenario == "ValueObjectFactory").Equivalent.ShouldBeTrue();
    }

    private static string WriteReport(params (string Method, double Mean, double AllocatedBytes)[] benchmarks)
    {
        var path = Path.Combine(Path.GetTempPath(), $"domainmap-benchmark-{Guid.NewGuid():N}.json");
        var report = new
        {
            Benchmarks = benchmarks.Select(x => new
            {
                x.Method,
                Statistics = new { x.Mean },
                Memory = new { BytesAllocatedPerOperation = x.AllocatedBytes },
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report));
        return path;
    }

    private static string WriteReportWithValues(params (string Method, double[] Values, double AllocatedBytes)[] benchmarks)
    {
        var path = Path.Combine(Path.GetTempPath(), $"domainmap-benchmark-{Guid.NewGuid():N}.json");
        var report = new
        {
            Benchmarks = benchmarks.Select(x => new
            {
                x.Method,
                Statistics = new { Mean = x.Values.Average(), OriginalValues = x.Values },
                Memory = new { BytesAllocatedPerOperation = x.AllocatedBytes },
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report));
        return path;
    }

    private static string BuildGeneratedMapper(
        string className,
        string mapOrderExpression,
        string placeExpression = "Factory(ToId(source))"
    ) =>
        $$"""
            partial class {{className}}
            {
                public static int MapFlat(int source) => source;
                public static int MapOrder(int source) => {{mapOrderExpression}};
                public static void UpdateFlat(int source, int target) { }
                public static int Place(int source) => {{placeExpression}};
                public static int MapId(int source) => ToId(source);
                private static int MapLine(int source) => source;
            }
            """;
}
