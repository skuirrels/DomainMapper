using BenchmarkDotNet.Running;
using DomainMapper.Benchmarks;

if (args is ["--write-comparison-parity", var domainMapperGenerated, var mapperlyGenerated, var declarations, var parityOutput])
{
    ComparisonCodeParity.Write(domainMapperGenerated, mapperlyGenerated, declarations, parityOutput);
    return 0;
}

if (args is ["--check-comparison", .. var reportPaths] && reportPaths.Length > 0)
{
    var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(reportPaths[0])) ?? Directory.GetCurrentDirectory();
    return ComparisonBenchmarkGate.Run(reportPaths, outputDirectory, ComparisonGateOptions.FromEnvironment());
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
