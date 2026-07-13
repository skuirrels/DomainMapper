using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DomainMap.Benchmarks;

internal sealed record ComparisonGateOptions(double MaxTimeRatio, double MaxAllocationRatio, double AllocationSlackBytes)
{
    public static ComparisonGateOptions FromEnvironment() =>
        new(
            ReadDouble("DOMAINMAP_MAX_MAPPERLY_TIME_RATIO", 1.25),
            ReadDouble("DOMAINMAP_MAX_MAPPERLY_ALLOCATION_RATIO", 1.10),
            ReadDouble("DOMAINMAP_MAPPERLY_ALLOCATION_SLACK_BYTES", 64)
        );

    private static double ReadDouble(string name, double defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value == null ? defaultValue : double.Parse(value, CultureInfo.InvariantCulture);
    }
}

internal sealed record BenchmarkComparison(
    string Scenario,
    double MapperlyMeanNanoseconds,
    double DomainMapMeanNanoseconds,
    double TimeRatio,
    double MapperlyAllocatedBytes,
    double DomainMapAllocatedBytes,
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

    public static int Run(string reportPath, string outputDirectory, ComparisonGateOptions options)
    {
        var result = Evaluate(reportPath, options);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "DomainMap-vs-Mapperly-gate.md"), BuildMarkdown(result, options));
        File.WriteAllText(
            Path.Combine(outputDirectory, "DomainMap-vs-Mapperly-gate.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
        );
        Console.WriteLine(BuildMarkdown(result, options));
        return result.Passed ? 0 : 1;
    }

    internal static ComparisonGateResult Evaluate(string reportPath, ComparisonGateOptions options)
    {
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        if (!report.RootElement.TryGetProperty("Benchmarks", out var benchmarksElement))
            return new ComparisonGateResult([], ["BenchmarkDotNet report does not contain a Benchmarks collection."]);

        var benchmarks = benchmarksElement
            .EnumerateArray()
            .Select(ReadBenchmark)
            .Where(x => x != null)
            .ToDictionary(x => x!.Method, x => x!, StringComparer.Ordinal);
        var mapperlyBenchmarks = benchmarks.Values.Where(x => x.Method.StartsWith(MapperlyPrefix, StringComparison.Ordinal)).ToArray();
        if (mapperlyBenchmarks.Length == 0)
            return new ComparisonGateResult([], ["No Mapperly comparison benchmarks were found."]);

        var comparisons = new List<BenchmarkComparison>();
        var errors = new List<string>();
        foreach (var mapperly in mapperlyBenchmarks)
        {
            var scenario = mapperly.Method[MapperlyPrefix.Length..];
            if (!benchmarks.TryGetValue(DomainMapPrefix + scenario, out var domainMap))
            {
                errors.Add($"Missing DomainMap benchmark pair for {mapperly.Method}.");
                continue;
            }

            comparisons.Add(Compare(scenario, mapperly, domainMap, options));
        }

        foreach (var domainMap in benchmarks.Values.Where(x => x.Method.StartsWith(DomainMapPrefix, StringComparison.Ordinal)))
        {
            var scenario = domainMap.Method[DomainMapPrefix.Length..];
            if (!benchmarks.ContainsKey(MapperlyPrefix + scenario))
                errors.Add($"Missing Mapperly benchmark pair for {domainMap.Method}.");
        }

        return new ComparisonGateResult(comparisons, errors);
    }

    private static BenchmarkComparison Compare(
        string scenario,
        BenchmarkMeasurement mapperly,
        BenchmarkMeasurement domainMap,
        ComparisonGateOptions options
    )
    {
        var timeRatio = domainMap.MeanNanoseconds / mapperly.MeanNanoseconds;
        var allowedAllocatedBytes = mapperly.AllocatedBytes * options.MaxAllocationRatio + options.AllocationSlackBytes;
        var failures = new List<string>();
        if (timeRatio > options.MaxTimeRatio)
            failures.Add($"time ratio {timeRatio:F3} exceeds {options.MaxTimeRatio:F3}");

        if (domainMap.AllocatedBytes > allowedAllocatedBytes)
        {
            failures.Add($"allocated bytes {domainMap.AllocatedBytes:F0} exceed allowed {allowedAllocatedBytes:F0}");
        }

        return new BenchmarkComparison(
            scenario,
            mapperly.MeanNanoseconds,
            domainMap.MeanNanoseconds,
            timeRatio,
            mapperly.AllocatedBytes,
            domainMap.AllocatedBytes,
            failures.Count == 0,
            failures.Count == 0 ? null : string.Join("; ", failures)
        );
    }

    private static BenchmarkMeasurement? ReadBenchmark(JsonElement benchmark)
    {
        if (!benchmark.TryGetProperty("Method", out var methodElement))
            return null;

        var method = methodElement.GetString();
        if (method == null || !benchmark.TryGetProperty("Statistics", out var statistics))
            return null;

        var mean = statistics.GetProperty("Mean").GetDouble();
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

        return new BenchmarkMeasurement(method, mean, allocatedBytes);
    }

    private static string BuildMarkdown(ComparisonGateResult result, ComparisonGateOptions options)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# DomainMap versus Mapperly performance gate");
        markdown.AppendLine();
        markdown.AppendLine(
            $"Limits: mean time <= {options.MaxTimeRatio:F2}x Mapperly; allocated bytes <= {options.MaxAllocationRatio:F2}x Mapperly + {options.AllocationSlackBytes:F0} B."
        );
        markdown.AppendLine();
        markdown.AppendLine("| Scenario | Mapperly mean | DomainMap mean | Time ratio | Mapperly alloc | DomainMap alloc | Result |");
        markdown.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | :---: |");
        foreach (var comparison in result.Comparisons)
        {
            markdown.AppendLine(
                $"| {comparison.Scenario} | {comparison.MapperlyMeanNanoseconds:F3} ns | {comparison.DomainMapMeanNanoseconds:F3} ns | {comparison.TimeRatio:F3}x | {comparison.MapperlyAllocatedBytes:F0} B | {comparison.DomainMapAllocatedBytes:F0} B | {(comparison.Passed ? "PASS" : "FAIL")} |"
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

    private sealed record BenchmarkMeasurement(string Method, double MeanNanoseconds, double AllocatedBytes);
}
