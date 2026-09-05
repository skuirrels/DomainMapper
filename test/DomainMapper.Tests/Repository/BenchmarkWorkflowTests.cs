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
    public void HostedTrendComparisonNeverComments()
    {
        // Hosted runner variance exceeded the old 150% alert threshold on a version-only change, so the trend is
        // recorded in the job summary and never posted as a pull request comment.
        var workflow = RepositoryFile.Read("benchmark.yml");

        workflow.ShouldContain("summary-always: true");
        workflow.ShouldContain("comment-on-alert: false");
        workflow.ShouldNotContain("alert-threshold");
        workflow.ShouldNotContain("comment-on-alert: >-");
    }
}
