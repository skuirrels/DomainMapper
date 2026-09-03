namespace DomainMapper.Tests.Repository;

internal static class RepositoryFile
{
    /// <summary>Reads a repository file linked into the test output under <c>Repository</c>.</summary>
    public static string Read(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Repository", fileName));
}
