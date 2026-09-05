namespace DomainMapper.Tests.Engine;

public sealed class FactoryBypassTests
{
    [Fact]
    public void WarnsOncePerMappingAndTargetWhenConventionConstructionBypassesAFactory()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Team MapTeam(TeamDto source);

                public static int Run() => MapTeam(new TeamDto(new List<CustomerDto> { new(Guid.NewGuid(), "a@x"), new(Guid.NewGuid(), "b@x") })).Members.Count;
            }

            public sealed class Customer
            {
                public Customer() { }
                public Guid Id { get; set; }
                public string Email { get; set; } = "";
                public static Customer Register(Guid id, string email) => new() { Id = id, Email = email };
            }
            public sealed record CustomerDto(Guid Id, string Email);
            public sealed record TeamDto(List<CustomerDto> Members);
            public sealed record Team(List<Customer> Members);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        var warnings = result.Diagnostics.Where(x => x.Id == "DMPR108").ToArray();
        warnings.Length.ShouldBe(1);
        warnings[0].GetMessage().ShouldContain("MapTeam");
        warnings[0].GetMessage().ShouldContain("'Customer'");
        warnings[0].GetMessage().ShouldContain("'Register'");
        GeneratorTestHarness.InvokeStatic<int>(result, "Mapper", "Run").ShouldBe(2);
    }

    [Fact]
    public void DoesNotWarnWhenConstructionGoesThroughATargetOrDomainFactory()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static CustomerId ToCustomerId(Guid value) => CustomerId.From(value);

                [MapToFactory(nameof(Customer.Register))]
                public static partial Customer Map(CustomerDto source);
            }

            public readonly record struct CustomerId(Guid Value)
            {
                public static CustomerId From(Guid value) => new(value);
            }
            public sealed class Customer
            {
                public Customer() { }
                public CustomerId Id { get; set; }
                public string Email { get; set; } = "";
                public static Customer Register(CustomerId id, string email) => new() { Id = id, Email = email };
            }
            public sealed record CustomerDto(Guid Id, string Email);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Diagnostics.ShouldNotContain(x => x.Id == "DMPR108");
    }

    [Fact]
    public void WarnsWhenASingleValueConstructorBypassesAFactory()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Customer Map(CustomerDto source);
            }

            public readonly record struct CustomerId(Guid Value)
            {
                public static CustomerId From(Guid value) => new(value);
            }
            public sealed record CustomerDto(Guid Id, string Name);
            public sealed record Customer(CustomerId Id, string Name);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Source.ShouldContain("new global::CustomerId(source.Id)");
        result.Diagnostics.ShouldContain(x => x.Id == "DMPR108" && x.GetMessage().Contains("'From'"));
    }

    [Fact]
    public void IgnoresStaticMembersThatAreNotFactories()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                public static partial Quote Map(QuoteDto source);
            }

            public sealed record Money(decimal Amount, string Currency)
            {
                public static Money Empty { get; } = new(0, "XXX");
                public static Money Zero() => Empty;
                public static Money Add(Money left, Money right) => left with { Amount = left.Amount + right.Amount };
                public static bool TryParse(string text, out Money? value) { value = null; return false; }
                public static T Create<T>(decimal amount) where T : class => null!;
                public static implicit operator Money(decimal amount) => new(amount, "XXX");
            }
            public sealed record MoneyDto(decimal Amount, string Currency);
            public sealed record QuoteDto(MoneyDto Price);
            public sealed record Quote(Money Price);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Diagnostics.ShouldNotContain(x => x.Id == "DMPR108");
    }

    [Fact]
    public void SuppressesTheWarningForNestedHelpersWithIgnoreTargetFactory()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using System.Collections.Generic;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [IgnoreTargetFactory(typeof(Customer), Reason = "EF Core entity; setters are the persistence write path")]
                public static partial Team MapTeam(TeamDto source);
            }

            public sealed class Customer
            {
                public Customer() { }
                public Guid Id { get; set; }
                public string Email { get; set; } = "";
                public static Customer Register(Guid id, string email) => new() { Id = id, Email = email };
            }
            public sealed record CustomerDto(Guid Id, string Email);
            public sealed record TeamDto(List<CustomerDto> Members);
            public sealed record Team(List<Customer> Members);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        result.Diagnostics.ShouldNotContain(x => x.Id == "DMPR108");
        result.Diagnostics.ShouldNotContain(x => x.Id == "DMPR102");
    }

    [Fact]
    public void ScopesIgnoreTargetFactoryToOneMappingMethod()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [IgnoreTargetFactory(typeof(Customer), Reason = "EF Core entity")]
                public static partial Customer ToEntity(CustomerDto source);

                public static partial Customer ToDomain(CustomerDto source);
            }

            public sealed class Customer
            {
                public Customer() { }
                public Guid Id { get; set; }
                public string Email { get; set; } = "";
                public static Customer Register(Guid id, string email) => new() { Id = id, Email = email };
            }
            public sealed record CustomerDto(Guid Id, string Email);
            """
        );

        result.Errors.ShouldBeEmpty(result.Source);
        var warnings = result.Diagnostics.Where(x => x.Id == "DMPR108").ToArray();
        warnings.Length.ShouldBe(1);
        warnings[0].GetMessage().ShouldContain("ToDomain");
        warnings[0].GetMessage().ShouldContain("[IgnoreTargetFactory(typeof(Customer))]");
    }

    [Fact]
    public void RejectsStaleIgnoreTargetFactoryDeclarations()
    {
        var result = GeneratorTestHarness.Generate(
            """
            using System;
            using DomainMapper.Abstractions;

            [DomainMapper]
            public static partial class Mapper
            {
                [DomainFactory(Input = DomainFactoryInput.Source)]
                private static CustomerId ToCustomerId(Guid value) => CustomerId.From(value);

                [IgnoreTargetFactory(typeof(CustomerId))]
                public static partial Customer Map(CustomerDto source);
            }

            public readonly record struct CustomerId(Guid Value)
            {
                public static CustomerId From(Guid value) => new(value);
            }
            public sealed record CustomerDto(Guid Id, string Name);
            public sealed record Customer(CustomerId Id, string Name);
            """
        );

        result.Diagnostics.ShouldContain(x =>
            x.Id == "DMPR102" && x.GetMessage().Contains("stale") && x.GetMessage().Contains("CustomerId")
        );
        result.Diagnostics.ShouldNotContain(x => x.Id == "DMPR108");
    }
}
