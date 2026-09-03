using System.Text.RegularExpressions;

namespace DomainMapper.Tests.Repository;

public sealed class VersionConsistencyTests
{
    private static readonly Regex ChangelogRelease = new(@"^## \[(?<version>\d+\.\d+\.\d+)\] - \d{4}-\d{2}-\d{2}$", RegexOptions.Multiline);
    private static readonly Regex DevelopmentVersion = new(@"\b\d+\.\d+\.\d+-dev\b");

    private static Version LatestRelease()
    {
        var match = ChangelogRelease.Match(RepositoryFile.Read("CHANGELOG.md"));
        match.Success.ShouldBeTrue("CHANGELOG.md must record at least one release");
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
    public void PackageValidationBaselineIsTheLatestReleasedVersion()
    {
        RepositoryFile
            .Read("DomainMapper.csproj")
            .ShouldContain($"<PackageValidationBaselineVersion>{LatestRelease()}</PackageValidationBaselineVersion>");
    }

    [Fact]
    public void DevelopmentVersionIsAheadOfTheLatestRelease()
    {
        var match = Regex.Match(RepositoryFile.Read("Directory.Build.props"), @"<Version>(?<version>\d+\.\d+\.\d+)-dev</Version>");

        match.Success.ShouldBeTrue("Directory.Build.props must declare a <Major.Minor.Patch>-dev version");
        Version.Parse(match.Groups["version"].Value).ShouldBeGreaterThan(LatestRelease());
    }

    [Theory]
    [InlineData("RELEASE_VERSION=\"1.2.0-dev.$GITHUB_RUN_ID\" ./build/package.sh")]
    [InlineData("-p:DomainMapperNugetPackageVersion=1.2.0-dev.$env:GITHUB_RUN_ID")]
    [InlineData("RELEASE_VERSION=${RELEASE_VERSION:-\"1.2.0-dev.$(date +%s)\"}")]
    public void DevelopmentVersionGuardMatchesHardcodedVersions(string offender) => DevelopmentVersion.IsMatch(offender).ShouldBeTrue();

    [Theory]
    [InlineData("test.yml")]
    [InlineData("package.sh")]
    public void BuildInfrastructureDoesNotHardcodeTheDevelopmentVersion(string fileName)
    {
        DevelopmentVersion
            .IsMatch(RepositoryFile.Read(fileName))
            .ShouldBeFalse($"{fileName} must derive the version from Directory.Build.props");
    }
}
