using System.Text.RegularExpressions;

namespace DomainMapper.Tests.Repository;

public sealed class VersionConsistencyTests
{
    private static readonly Regex ChangelogRelease = new(@"^## \[(?<version>\d+\.\d+\.\d+)\] - \d{4}-\d{2}-\d{2}$", RegexOptions.Multiline);
    private static readonly Regex DevelopmentVersionPattern = new(@"\b\d+\.\d+\.\d+-dev\b");

    /// <summary>Released versions recorded in the changelog, newest first.</summary>
    private static Version[] Releases()
    {
        var releases = ChangelogRelease
            .Matches(RepositoryFile.Read("CHANGELOG.md"))
            .Select(x => Version.Parse(x.Groups["version"].Value))
            .ToArray();
        releases.ShouldNotBeEmpty("CHANGELOG.md must record at least one release");
        return releases;
    }

    private static Version LatestRelease() => Releases()[0];

    private static Version DevelopmentVersion()
    {
        var match = Regex.Match(RepositoryFile.Read("Directory.Build.props"), @"<Version>(?<version>\d+\.\d+\.\d+)-dev</Version>");
        match.Success.ShouldBeTrue("Directory.Build.props must declare a <Major.Minor.Patch>-dev version");
        return Version.Parse(match.Groups["version"].Value);
    }

    [Fact]
    public void ReadmeInstallsTheLatestReleasedVersion()
    {
        var latest = LatestRelease();
        var readme = RepositoryFile.Read("README.md");

        readme.ShouldContain($"## Version {latest}");
        readme.ShouldContain($"dotnet add package DomainMapper --version {latest}");
    }

    [Fact]
    public void PackageValidationBaselineIsTheNewestReleaseOlderThanTheDevelopmentVersion()
    {
        // The version under development is not on NuGet yet, so validation must compare against the release before it.
        var developmentVersion = DevelopmentVersion();
        var baseline = Releases().FirstOrDefault(x => x < developmentVersion);

        baseline.ShouldNotBeNull($"CHANGELOG.md must record a release older than {developmentVersion}");
        RepositoryFile
            .Read("DomainMapper.csproj")
            .ShouldContain($"<PackageValidationBaselineVersion>{baseline}</PackageValidationBaselineVersion>");
    }

    [Fact]
    public void DevelopmentVersionIsNotBehindTheLatestRelease() => DevelopmentVersion().ShouldBeGreaterThanOrEqualTo(LatestRelease());

    [Theory]
    [InlineData("RELEASE_VERSION=\"1.2.0-dev.$GITHUB_RUN_ID\" ./build/package.sh")]
    [InlineData("-p:DomainMapperNugetPackageVersion=1.2.0-dev.$env:GITHUB_RUN_ID")]
    [InlineData("RELEASE_VERSION=${RELEASE_VERSION:-\"1.2.0-dev.$(date +%s)\"}")]
    public void DevelopmentVersionGuardMatchesHardcodedVersions(string offender) =>
        DevelopmentVersionPattern.IsMatch(offender).ShouldBeTrue();

    [Theory]
    [InlineData("test.yml")]
    [InlineData("package.sh")]
    public void BuildInfrastructureDoesNotHardcodeTheDevelopmentVersion(string fileName)
    {
        DevelopmentVersionPattern
            .IsMatch(RepositoryFile.Read(fileName))
            .ShouldBeFalse($"{fileName} must derive the version from Directory.Build.props");
    }
}
