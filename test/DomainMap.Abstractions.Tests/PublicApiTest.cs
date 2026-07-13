using PublicApiGenerator;

namespace DomainMap.Abstractions.Tests;

public class PublicApiTest
{
    [Fact]
    public Task PublicApiHasNotChanged()
    {
        var assembly = typeof(DomainMapperAttribute).Assembly;
        var api = assembly.GeneratePublicApi();
        return Verify(api, "cs");
    }
}
