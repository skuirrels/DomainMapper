namespace DomainMapper.Tests.Repository;

public sealed class ReleaseWorkflowTests
{
    [Fact]
    public void NuGetVersionIsDerivedFromTheReleaseTag()
    {
        var workflow = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "release.yml"));

        workflow.ShouldContain("RELEASE_TAG: ${{ github.event.release.tag_name || inputs.tag }}");
        workflow.ShouldContain("RELEASE_VERSION=${RELEASE_TAG#v}");
        workflow.ShouldContain("gh release view \"$RELEASE_TAG\"");
        workflow.ShouldContain("gh release upload \"$RELEASE_TAG\"");
        workflow.ShouldNotContain("gh release view '${{ github.event.release.tag_name }}'");
        workflow.ShouldNotContain("gh release upload \"${{ github.event.release.tag_name }}\"");
        workflow.ShouldNotContain("github.event.release.name");
    }

    [Fact]
    public void NuGetPublishingUsesOnlyTheStableOidcEnvironment()
    {
        var workflow = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "release.yml"));

        workflow.ShouldContain("workflow_dispatch:");
        workflow.ShouldContain("if: ${{ github.event_name == 'workflow_dispatch' || github.event.release.prerelease == false }}");
        workflow.ShouldContain("environment: stable");
        workflow.ShouldContain("id-token: write");
        workflow.ShouldContain("uses: NuGet/login@v1");
        workflow.ShouldContain("NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}");
        workflow.ShouldNotContain("NUGET_API_TOKEN");
        workflow.ShouldNotContain("environment: next");
        workflow.ShouldNotContain("CLOUDFLARE");
        workflow.ShouldNotContain("uses: ./.github/workflows/docs.yml");
    }
}
