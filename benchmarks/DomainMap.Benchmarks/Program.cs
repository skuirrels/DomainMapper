using BenchmarkDotNet.Running;
using DomainMap.Benchmarks;

if (args is ["--check-comparison", var reportPath])
{
    var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? Directory.GetCurrentDirectory();
    return ComparisonBenchmarkGate.Run(reportPath, outputDirectory, ComparisonGateOptions.FromEnvironment());
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
