namespace DomainMapper.Tests.Repository;

public sealed class ReleaseWorkflowTests
{
    [Fact]
    public void NuGetVersionIsDerivedFromTheReleaseTag()
    {
        var workflow = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "release.yml"));

        workflow.ShouldContain("RELEASE_TAG: ${{ github.event.release.tag_name }}");
        workflow.ShouldContain("RELEASE_VERSION=${RELEASE_TAG#v}");
        workflow.ShouldContain("gh release view \"$RELEASE_TAG\"");
        workflow.ShouldContain("gh release upload \"$RELEASE_TAG\"");
        workflow.ShouldNotContain("gh release view '${{ github.event.release.tag_name }}'");
        workflow.ShouldNotContain("gh release upload \"${{ github.event.release.tag_name }}\"");
        workflow.ShouldNotContain("github.event.release.name");
    }
}
