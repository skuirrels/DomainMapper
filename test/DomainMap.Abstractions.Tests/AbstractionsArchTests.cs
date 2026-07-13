using DomainMap.Abstractions.Tests.Helpers;
using NetArchTest.Rules;

namespace DomainMap.Abstractions.Tests;

public class AbstractionsArchTests
{
    [Fact]
    public void AbstractionsShouldBeSealed()
    {
        Types
            .InAssembly(typeof(DomainMapperAttribute).Assembly)
            .That()
            .AreNotInterfaces()
            .And()
            // exclude DomainMapperAttribute since it is a lot easier to handle the defaults
            // when it can be inherited.
            .DoNotHaveName(nameof(DomainMapperAttribute))
            .Should()
            .BeSealed()
            .GetResult()
            .ShouldHaveNoViolations();
    }

    [Fact]
    public void AttributesShouldHaveConditionalAttribute()
    {
        Types
            .InAssembly(typeof(DomainMapperAttribute).Assembly)
            .That()
            .Inherit(typeof(Attribute))
            .Should()
            .MeetCustomRule(new ConditionalAttributeSymbolRule("DOMAINMAP_ABSTRACTIONS_SCOPE_RUNTIME"))
            .GetResult()
            .ShouldHaveNoViolations();
    }
}
