using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DomainMap.Benchmarks;

internal sealed record ComparisonGateOptions(
    double MaxTimeRatio,
    double TimeSlackNanoseconds,
    double MaxAllocationRatio,
    double AllocationSlackBytes
)
{
    private const string ParityReportEnvironmentVariable = "DOMAINMAP_MAPPERLY_PARITY_REPORT";
    private const string FasterScenariosEnvironmentVariable = "DOMAINMAP_MAPPERLY_FASTER_SCENARIOS";

    public IReadOnlySet<string> ProvenParityScenarios { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlySet<string> RequireFasterScenarios { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public double ConfidenceZScore { get; init; } = 1.645;

    public static ComparisonGateOptions FromEnvironment()
    {
        var parityReportPath = Environment.GetEnvironmentVariable(ParityReportEnvironmentVariable);
        var parityScenarios = string.IsNullOrWhiteSpace(parityReportPath)
            ? new HashSet<string>(StringComparer.Ordinal)
            : ComparisonCodeParity.Read(parityReportPath).EquivalentScenarios;

        return new ComparisonGateOptions(
            ReadDouble("DOMAINMAP_MAX_MAPPERLY_TIME_RATIO", 1.25),
            ReadDouble("DOMAINMAP_MAPPERLY_TIME_SLACK_NS", 1),
            ReadDouble("DOMAINMAP_MAX_MAPPERLY_ALLOCATION_RATIO", 1.10),
            ReadDouble("DOMAINMAP_MAPPERLY_ALLOCATION_SLACK_BYTES", 64)
        )
        {
            ProvenParityScenarios = parityScenarios,
            RequireFasterScenarios = ReadSet(FasterScenariosEnvironmentVariable),
            ConfidenceZScore = ReadDouble("DOMAINMAP_MAPPERLY_CONFIDENCE_Z", 1.645),
        };
    }

    private static IReadOnlySet<string> ReadSet(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    private static double ReadDouble(string name, double defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value == null ? defaultValue : double.Parse(value, CultureInfo.InvariantCulture);
    }
}

internal sealed record BenchmarkComparison(
    string Scenario,
    string Expectation,
    double MapperlyMedianNanoseconds,
    double DomainMapMedianNanoseconds,
    double TimeRatio,
    double UpperDifferenceConfidenceBoundNanoseconds,
    double MapperlyAllocatedBytes,
    double DomainMapAllocatedBytes,
    int ReportCount,
    int MapperlySampleCount,
    int DomainMapSampleCount,
    bool Passed,
    string? Failure
);

internal sealed record ComparisonGateResult(IReadOnlyList<BenchmarkComparison> Comparisons, IReadOnlyList<string> Errors)
{
    public bool Passed => Errors.Count == 0 && Comparisons.All(x => x.Passed);
}

internal static class ComparisonBenchmarkGate
{
    private const string DomainMapPrefix = "DomainMap";
    private const string MapperlyPrefix = "Mapperly";

    public static int Run(string reportPath, string outputDirectory, ComparisonGateOptions options) =>
        Run([reportPath], outputDirectory, options);

    public static int Run(IReadOnlyList<string> reportPaths, string outputDirectory, ComparisonGateOptions options)
    {
        var result = Evaluate(reportPaths, options);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "DomainMap-vs-Mapperly-gate.md"), BuildMarkdown(result, options));
        File.WriteAllText(
            Path.Combine(outputDirectory, "DomainMap-vs-Mapperly-gate.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
        );
        Console.WriteLine(BuildMarkdown(result, options));
        return result.Passed ? 0 : 1;
    }

    internal static ComparisonGateResult Evaluate(string reportPath, ComparisonGateOptions options) => Evaluate([reportPath], options);

    internal static ComparisonGateResult Evaluate(IReadOnlyList<string> reportPaths, ComparisonGateOptions options)
    {
        if (reportPaths.Count == 0)
            return new ComparisonGateResult([], ["No benchmark reports were provided."]);

        var reportRuns = reportPaths.Select(ReadReport).ToArray();
        var allMethodNames = reportRuns.SelectMany(x => x.Keys).ToHashSet(StringComparer.Ordinal);
        var mapperlyMethods = allMethodNames.Where(x => x.StartsWith(MapperlyPrefix, StringComparison.Ordinal)).Order().ToArray();
        if (mapperlyMethods.Length == 0)
            return new ComparisonGateResult([], ["No Mapperly comparison benchmarks were found."]);

        var comparisons = new List<BenchmarkComparison>();
        var errors = new List<string>();
        foreach (var mapperlyMethod in mapperlyMethods)
        {
            var scenario = mapperlyMethod[MapperlyPrefix.Length..];
            var domainMapMethod = DomainMapPrefix + scenario;
            if (!allMethodNames.Contains(domainMapMethod))
            {
                errors.Add($"Missing DomainMap benchmark pair for {mapperlyMethod}.");
                continue;
            }

            var pairedRuns = new List<(BenchmarkRunMeasurement Mapperly, BenchmarkRunMeasurement DomainMap)>();
            for (var reportIndex = 0; reportIndex < reportRuns.Length; reportIndex++)
            {
                var report = reportRuns[reportIndex];
                var hasMapperly = report.TryGetValue(mapperlyMethod, out var mapperly);
                var hasDomainMap = report.TryGetValue(domainMapMethod, out var domainMap);
                if (!hasMapperly || !hasDomainMap)
                {
                    errors.Add($"Report {reportPaths[reportIndex]} does not contain a complete pair for {scenario}.");
                    continue;
                }

                pairedRuns.Add((mapperly!, domainMap!));
            }

            if (pairedRuns.Count > 0)
                comparisons.Add(Compare(scenario, pairedRuns, options));
        }

        foreach (var domainMapMethod in allMethodNames.Where(x => x.StartsWith(DomainMapPrefix, StringComparison.Ordinal)))
        {
            var scenario = domainMapMethod[DomainMapPrefix.Length..];
            if (!allMethodNames.Contains(MapperlyPrefix + scenario))
                errors.Add($"Missing Mapperly benchmark pair for {domainMapMethod}.");
        }

        return new ComparisonGateResult(comparisons, errors);
    }

    private static BenchmarkComparison Compare(
        string scenario,
        IReadOnlyList<(BenchmarkRunMeasurement Mapperly, BenchmarkRunMeasurement DomainMap)> pairedRuns,
        ComparisonGateOptions options
    )
    {
        var mapperlyValues = pairedRuns.SelectMany(x => x.Mapperly.Values).ToArray();
        var domainMapValues = pairedRuns.SelectMany(x => x.DomainMap.Values).ToArray();
        var mapperlyMedian = Median(mapperlyValues);
        var domainMapMedian = Median(domainMapValues);
        var timeRatio = domainMapMedian / mapperlyMedian;
        var upperDifferenceBound = DifferenceUpperConfidenceBound(mapperlyValues, domainMapValues, options.ConfidenceZScore);
        var mapperlyAllocatedBytes = pairedRuns.Max(x => x.Mapperly.AllocatedBytes);
        var domainMapAllocatedBytes = pairedRuns.Max(x => x.DomainMap.AllocatedBytes);
        var failures = new List<string>();

        string expectation;
        if (options.ProvenParityScenarios.Contains(scenario))
        {
            expectation = "PROVEN PARITY";
        }
        else if (options.RequireFasterScenarios.Contains("*") || options.RequireFasterScenarios.Contains(scenario))
        {
            expectation = "FASTER";
            if (domainMapMedian >= mapperlyMedian || upperDifferenceBound >= 0)
            {
                failures.Add(
                    $"DomainMap is not statistically faster: median {domainMapMedian:F3} ns versus {mapperlyMedian:F3} ns; "
                        + $"one-sided confidence bound for DomainMap minus Mapperly is {upperDifferenceBound:F3} ns"
                );
            }
        }
        else
        {
            expectation = "REGRESSION LIMIT";
            var allowedMedianNanoseconds = mapperlyMedian * options.MaxTimeRatio + options.TimeSlackNanoseconds;
            if (domainMapMedian > allowedMedianNanoseconds)
            {
                failures.Add(
                    $"median time {domainMapMedian:F3} ns exceeds allowed {allowedMedianNanoseconds:F3} ns "
                        + $"({options.MaxTimeRatio:F3}x plus {options.TimeSlackNanoseconds:F3} ns slack; time ratio {timeRatio:F3})"
                );
            }
        }

        for (var runIndex = 0; runIndex < pairedRuns.Count; runIndex++)
        {
            var mapperlyAllocation = pairedRuns[runIndex].Mapperly.AllocatedBytes;
            var domainMapAllocation = pairedRuns[runIndex].DomainMap.AllocatedBytes;
            var allowedAllocation = mapperlyAllocation * options.MaxAllocationRatio + options.AllocationSlackBytes;
            if (domainMapAllocation > allowedAllocation)
            {
                failures.Add(
                    $"run {runIndex + 1} allocated bytes {domainMapAllocation:F0} exceed allowed {allowedAllocation:F0} "
                        + $"from Mapperly's {mapperlyAllocation:F0} B"
                );
            }
        }

        return new BenchmarkComparison(
            scenario,
            expectation,
            mapperlyMedian,
            domainMapMedian,
            timeRatio,
            upperDifferenceBound,
            mapperlyAllocatedBytes,
            domainMapAllocatedBytes,
            pairedRuns.Count,
            mapperlyValues.Length,
            domainMapValues.Length,
            failures.Count == 0,
            failures.Count == 0 ? null : string.Join("; ", failures)
        );
    }

    private static IReadOnlyDictionary<string, BenchmarkRunMeasurement> ReadReport(string reportPath)
    {
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        if (!report.RootElement.TryGetProperty("Benchmarks", out var benchmarksElement))
            return new Dictionary<string, BenchmarkRunMeasurement>(StringComparer.Ordinal);

        return benchmarksElement
            .EnumerateArray()
            .Select(ReadBenchmark)
            .Where(x => x != null)
            .ToDictionary(x => x!.Method, x => x!, StringComparer.Ordinal);
    }

    private static BenchmarkRunMeasurement? ReadBenchmark(JsonElement benchmark)
    {
        if (!benchmark.TryGetProperty("Method", out var methodElement))
            return null;

        var method = methodElement.GetString();
        if (method == null || !benchmark.TryGetProperty("Statistics", out var statistics))
            return null;

        double[] values;
        if (statistics.TryGetProperty("OriginalValues", out var originalValues) && originalValues.ValueKind == JsonValueKind.Array)
            values = originalValues.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        else
            values = [statistics.GetProperty("Mean").GetDouble()];

        var allocatedBytes = 0d;
        if (
            benchmark.TryGetProperty("Memory", out var memory)
            && memory.ValueKind == JsonValueKind.Object
            && memory.TryGetProperty("BytesAllocatedPerOperation", out var allocated)
            && allocated.ValueKind == JsonValueKind.Number
        )
        {
            allocatedBytes = allocated.GetDouble();
        }

        return new BenchmarkRunMeasurement(method, values, allocatedBytes);
    }

    private static double Median(IReadOnlyCollection<double> values)
    {
        var ordered = values.Order().ToArray();
        var midpoint = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[midpoint - 1] + ordered[midpoint]) / 2 : ordered[midpoint];
    }

    private static double DifferenceUpperConfidenceBound(
        IReadOnlyCollection<double> mapperlyValues,
        IReadOnlyCollection<double> domainMapValues,
        double zScore
    )
    {
        var mapperlyMean = mapperlyValues.Average();
        var domainMapMean = domainMapValues.Average();
        var standardError = Math.Sqrt(
            SampleVariance(mapperlyValues) / mapperlyValues.Count + SampleVariance(domainMapValues) / domainMapValues.Count
        );
        return domainMapMean - mapperlyMean + zScore * standardError;
    }

    private static double SampleVariance(IReadOnlyCollection<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        return values.Sum(x => Math.Pow(x - mean, 2)) / (values.Count - 1);
    }

    private static string BuildMarkdown(ComparisonGateResult result, ComparisonGateOptions options)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# DomainMap versus Mapperly performance gate");
        markdown.AppendLine();
        markdown.AppendLine(
            $"Regression limits: median time <= {options.MaxTimeRatio:F2}x Mapperly + {options.TimeSlackNanoseconds:F2} ns; "
                + $"allocated bytes in every run <= {options.MaxAllocationRatio:F2}x Mapperly + {options.AllocationSlackBytes:F0} B."
        );
        markdown.AppendLine();
        markdown.AppendLine(
            "| Scenario | Expectation | Mapperly median | DomainMap median | Time ratio | Upper difference bound | Reports / samples | Allocation | Result |"
        );
        markdown.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |");
        foreach (var comparison in result.Comparisons)
        {
            markdown.AppendLine(
                $"| {comparison.Scenario} | {comparison.Expectation} | {comparison.MapperlyMedianNanoseconds:F3} ns | "
                    + $"{comparison.DomainMapMedianNanoseconds:F3} ns | {comparison.TimeRatio:F3}x | "
                    + $"{comparison.UpperDifferenceConfidenceBoundNanoseconds:F3} ns | "
                    + $"{comparison.ReportCount} / {comparison.MapperlySampleCount}:{comparison.DomainMapSampleCount} | "
                    + $"{comparison.MapperlyAllocatedBytes:F0} B / {comparison.DomainMapAllocatedBytes:F0} B | "
                    + $"{(comparison.Passed ? "PASS" : "FAIL")} |"
            );
            if (comparison.Failure != null)
                markdown.AppendLine($"\n- {comparison.Scenario}: {comparison.Failure}");
        }

        foreach (var error in result.Errors)
        {
            markdown.AppendLine($"\n- {error}");
        }

        markdown.AppendLine();
        markdown.AppendLine(result.Passed ? "**Gate passed.**" : "**Gate failed.**");
        return markdown.ToString();
    }

    private sealed record BenchmarkRunMeasurement(string Method, double[] Values, double AllocatedBytes);
}
