using DomainMap.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace DomainMap.Tests.Mapping;

public class MapperTest
{
    [Fact]
    public Task SameMapperNameInMultipleNamespacesShouldWork()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            namespace Test.A
            {
                [DomainMapper]
                internal partial class FooBarMapper
                {
                    internal partial string FooToBar(string value);
                }
            }

            namespace Test.B
            {
                [DomainMapper]
                internal partial class FooBarMapper
                {
                    internal partial string FooToBar(string value);
                }
            }
            """
        );

        return TestHelper.VerifyGenerator(source);
    }

    [Fact]
    public Task MapperInNestedClassAndNamespaceShouldWork()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            namespace FooBar;

            public static partial class CarFeature
            {
                public static partial class Mappers
                {
                    [DomainMapper]
                    public partial class CarMapper
                    {
                        public partial double ToDouble(int value);
                    }
                }
            }
            """
        );

        return TestHelper.VerifyGenerator(source);
    }

    [Fact]
    public Task MapperInNestedClassShouldWork()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            public static partial class CarFeature
            {
                public static partial class Mappers
                {
                    [DomainMapper]
                    public partial class CarMapper
                    {
                        public partial double ToDouble(int value);
                    }
                }
            }
            """
        );

        return TestHelper.VerifyGenerator(source);
    }

    [Fact]
    public Task MapperInNestedClassesWithAttributesShouldWork()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            public static partial class CarFeature
            {
                [Obsolete]
                public static partial class Mappers
                {
                    [DomainMapper]
                    public partial class CarMapper
                    {
                        public partial double ToDouble(int value);
                    }
                }
            }
            """
        );

        return TestHelper.VerifyGenerator(source);
    }

    [Fact]
    public Task MapperInNestedClassesWithBaseTypeShouldWork()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            public abstract class BaseClass { }

            public partial class CarFeature : BaseClass
            {
                public partial class Mappers : BaseClass
                {
                    [DomainMapper]
                    public partial class CarMapper
                    {
                        public partial double ToDouble(int value);
                    }
                }
            }
            """
        );

        return TestHelper.VerifyGenerator(source);
    }

    [Fact]
    public void LanguageLevelLower9ShouldDiagnostic()
    {
        var source = TestSourceBuilder.Mapping("string", "int");
        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics with { LanguageVersion = LanguageVersion.CSharp8 })
            .Should()
            .HaveDiagnostic(
                DiagnosticDescriptors.LanguageVersionNotSupported,
                "DomainMap does not support the C# language version 8.0 but requires at C# least version 9.0"
            )
            .HaveAssertedAllDiagnostics();
    }

    [Fact]
    public Task AssemblyAttributeShouldWork()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            [assembly: MapperDefaultsAttribute(EnumMappingIgnoreCase = true)]
            [DomainMapper(EnumMappingStrategy = EnumMappingStrategy.ByName)]
            public partial class MyMapper
            {
                private partial E2 Map(E1 source);
            }

            enum E1 { value1 }
            enum E2 { Value1 }
            """
        );

        return TestHelper.VerifyGenerator(source);
    }
}
