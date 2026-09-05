namespace DomainMapper.Tests.Repository;

public sealed class BenchmarkWorkflowTests
{
    [Fact]
    public void HostedTrendComparisonIsInformational()
    {
        // Absolute timings drift across hosted runner generations; only the paired same-run gate may fail the job.
        var workflow = RepositoryFile.Read("benchmark.yml");

        workflow.ShouldContain("name: Enforce Mapperly no-regression policy");
        workflow.ShouldContain("--check-comparison");
        workflow.ShouldContain("fail-on-alert: false");
        workflow.ShouldNotContain("fail-on-alert: true");
        workflow.ShouldNotContain("fail-threshold");
    }

    [Fact]
    public void HostedTrendCommentsSkipDependabot()
    {
        RepositoryFile.Read("benchmark.yml").ShouldContain("github.actor != 'dependabot[bot]'");
    }
}
