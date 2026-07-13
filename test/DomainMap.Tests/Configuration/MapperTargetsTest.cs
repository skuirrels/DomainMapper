using System.Reflection;
using System.Text.RegularExpressions;
using DomainMap.Abstractions;

namespace DomainMap.Tests.Configuration;

public class MapperTargetsTest
{
    [Fact]
    public void TargetsFileShouldContainCompilerVisibleProperties()
    {
        var properties = typeof(DomainMapperAttribute)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => "DomainMap" + p.Name)
            .ToHashSet();

        var targetsFilePath = Path.Combine(SourcePaths.GetSolutionDirectory(), "src", "DomainMap", "DomainMap.targets");

        File.Exists(targetsFilePath).ShouldBeTrue($"File not found: {targetsFilePath}");

        var targetsContent = File.ReadAllText(targetsFilePath);
        var matches = Regex
            .Matches(targetsContent, "CompilerVisibleProperty Include=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        // if this does not match,
        // likely a CompilerVisibleProperty is missing in the DomainMap.targets file
        // or one is left over which was removed in the DomainMapAttribute.
        matches.ShouldBe(properties, ignoreOrder: true);
    }
}
