using DomainMap.Diagnostics;

namespace DomainMap.Tests.Mapping;

public class UseStaticDomainMapperTest
{
    [Fact]
    public void UseStaticGenericMapperStaticMethod()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public static BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(global::OtherMapper.ToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void UseStaticTypeOfMapperStaticMethod()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public static BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            [UseStaticDomainMapper(typeof(OtherMapper))]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(global::OtherMapper.ToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void UseStaticGenericMapperStaticMethodInStaticMapper()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            static class OtherMapper { public static BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            [UseStaticDomainMapper(typeof(OtherMapper))]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(global::OtherMapper.ToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public Task ReferenceHandling()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using DomainMap.Abstractions.ReferenceHandling;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public static BExternal ToBExternal(AExternal source, [ReferenceHandler] IReferenceHandler refHandler) => new BExternal(); }

            [DomainMapper(UseReferenceHandling = true)]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                private partial B Map(A source, [ReferenceHandler] IReferenceHandler refHandler);
            }
            """
        );
        return TestHelper.VerifyGenerator(source);
    }

    [Fact]
    public Task ReferenceHandlingEnabledNoParameter()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using DomainMap.Abstractions.ReferenceHandling;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public static BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper(UseReferenceHandling = true)]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                private partial B Map(A source, [ReferenceHandler] IReferenceHandler refHandler);
            }
            """
        );
        return TestHelper.VerifyGenerator(source);
    }

    [Fact]
    public void IgnoreInstanceMethod()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using DomainMap.Abstractions.ReferenceHandling;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(MapToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void IgnorePrivateMethod()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using DomainMap.Abstractions.ReferenceHandling;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { private BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(MapToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void UseGeneratedMapper()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using DomainMap.Abstractions.ReferenceHandling;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            [DomainMapper]
            partial class OtherMapper { public partial BExternal ToBExternal(AExternal source); }

            [DomainMapper]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(MapToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void IgnoreInvalidSignature()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using DomainMap.Abstractions.ReferenceHandling;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            public class OtherMapper { public void NotAMappingMethod(AExternal source) {} }

            [DomainMapper]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(MapToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void PreferInternalMapping()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public static BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                partial B Map(A source);

                private partial BExternal MapInternal(AExternal source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(MapInternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void PreferInternalImplementedMapping()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public static BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                partial B Map(A source);

                private BExternal MapInternal(AExternal source) = new BExternal();
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(MapInternal(source.Value));
                return target;
                """
            );
    }

    private MapperGenerationResultAssertions ExecuteStaticGenericMapperStaticMethodFromAnotherAssemblyCompilation(
        bool asCompilationReference
    )
    {
        var testDependencySource = TestSourceBuilder.SyntaxTree(
            """
            using System;
            using DomainMap.Abstractions;

            namespace DomainMap.TestDependency.Mapper
            {
                [DomainMapper]
                public static partial class DateTimeMapper
                {
                    public static DateTimeOffset MapToDateTimeOffset(DateTime dateTime) => new(dateTime, TimeSpan.Zero);
                }
            }
            """
        );

        using var testDependencyAssembly = TestHelper.BuildAssembly(
            "DomainMap.TestDependency",
            asCompilationReference,
            testDependencySource
        );

        var source = TestSourceBuilder.CSharp(
            """
            using System;
            using System.Linq;
            using DomainMap.Abstractions;
            using DomainMap.TestDependency.Mapper;

            [DomainMapper]
            [UseStaticDomainMapper(typeof(DateTimeMapper))]
            public static partial class Mapper
            {
                public static partial IQueryable<Target> ProjectToTarget(IQueryable<Source> source);

                public static partial Target MapToTarget(Source source);

                public class Source
                {
                    public DateTime DateTime { get; set; }
                }

                public class Target
                {
                    public DateTimeOffset DateTime { get; set; }
                }
            }
            """
        );

        return TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics, additionalAssemblies: [testDependencyAssembly])
            .Should();
    }

    /// <summary>
    /// This tests a situation when your IDE runs the source generator (references are other syntax trees)
    /// </summary>
    [Fact]
    public void UseStaticGenericMapperStaticMethodFromAnotherAssemblyAsReference()
    {
        var result = ExecuteStaticGenericMapperStaticMethodFromAnotherAssemblyCompilation(asCompilationReference: true);

        result.HaveMethodBody(
            "ProjectToTarget",
            """
            #nullable disable
                    return global::System.Linq.Queryable.Select(
                        source,
                        x => new global::Mapper.Target()
                        {
                            DateTime = new global::System.DateTimeOffset(x.DateTime, global::System.TimeSpan.Zero),
                        }
                    );
            #nullable enable
            """
        );
    }

    /// <summary>
    /// This tests a situation when compiler produces final assembly (references are compiled assemblies)
    /// </summary>
    [Fact]
    public void UseStaticGenericMapperStaticMethodFromAnotherAssemblyAsCompiledAssembly()
    {
        var result = ExecuteStaticGenericMapperStaticMethodFromAnotherAssemblyCompilation(asCompilationReference: false);

        result
            .HaveDiagnostic(DiagnosticDescriptors.QueryableProjectionMappingCannotInline)
            .HaveMethodBody(
                "ProjectToTarget",
                """
                #nullable disable
                        return global::System.Linq.Queryable.Select(
                            source,
                            x => new global::Mapper.Target()
                            {
                                DateTime = global::DomainMap.TestDependency.Mapper.DateTimeMapper.MapToDateTimeOffset(x.DateTime),
                            }
                        );
                #nullable enable
                """
            );
    }

    [Fact]
    public void ExternalMappingDoesNotAffectOnUseStaticDomainMapper()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using DomainMap.Abstractions.ReferenceHandling;

            record A(int Value);
            record B(int Value);

            class OtherMapper { public static int AutoMap(int source) => source + 1; }
            class ExternalMapper { public static int ExplicitMap(int source) => source + 2; }

            [DomainMapper]
            [UseStaticDomainMapper<OtherMapper>]
            public partial class Mapper
            {
                partial B Map(A source);

                [MapProperty("Value", "Value", Use = nameof(@ExternalMapper.ExplicitMap)]
                partial B MapOther(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(global::OtherMapper.AutoMap(source.Value));
                return target;
                """
            )
            .HaveMethodBody(
                "MapOther",
                """
                var target = new global::B(global::ExternalMapper.ExplicitMap(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void AssemblyLevelUseStaticGenericMapperStaticMethod()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            [assembly: UseStaticDomainMapper<OtherMapper>]

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public static BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(global::OtherMapper.ToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void AssemblyLevelUseStaticDomainMapperStaticMethod()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            [assembly: UseStaticDomainMapper(typeof(OtherMapper))]

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            class OtherMapper { public static BExternal ToBExternal(AExternal source) => new BExternal(); }

            [DomainMapper]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(global::OtherMapper.ToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public void CombineAssemblyLevelUseStaticDomainMappers()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            [assembly: UseStaticDomainMapper(typeof(OtherMapper))]
            [assembly: UseStaticDomainMapper<AnotherMapper>]

            record A(int Value1, long Value2);
            record B(string Value1, string Value2);

            class OtherMapper { public static string IntToString(int source) => source.ToString(); }
            class AnotherMapper { public static string LongToString(long source) => source.ToString(); }

            [DomainMapper]
            public partial class Mapper
            {
                partial B Map(A source);
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(
                    global::OtherMapper.IntToString(source.Value1),
                    global::AnotherMapper.LongToString(source.Value2)
                );
                return target;
                """
            );
    }

    [Fact]
    public void SelfReferencingUseStaticDomainMapperStaticMethodNotCauseDiagnostic()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            [assembly: UseStaticDomainMapper(typeof(Mapper))]

            record A(AExternal Value);
            record B(BExternal Value);
            record AExternal();
            record BExternal();

            [DomainMapper]
            public partial class Mapper
            {
                partial B Map(A source);

                public static BExternal ToBExternal(AExternal source) => new BExternal();
            }
            """
        );
        TestHelper
            .GenerateMapper(source)
            .Should()
            .HaveMapMethodBody(
                """
                var target = new global::B(ToBExternal(source.Value));
                return target;
                """
            );
    }

    [Fact]
    public Task ProjectionWithUseStaticDomainMapperShouldInlineGenerator()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using System.Linq;

            public class Maker
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public class Car
            {
                public int Id { get; set; }
                public string Name { get; set; } = null!;
                public Maker Make { get; set; } = null!;
            }

            public record MakerDto
            {
                public int Id { get; init; }
                public string MakerName { get; init; } = null!;
            }

            public record CarDto
            {
                public int Id { get; init; }
                public string CarName { get; init; } = null!;
                public MakerDto Maker { get; init; } = null!;
            }

            [DomainMapper]
            public static partial class OtherMapper
            {
                public static partial IQueryable<MakerDto> ProjectToMakerDto(this IQueryable<Maker> query);

                [MapperRequiredMapping(RequiredMappingStrategy.Target)]
                [MapProperty(nameof(Maker.Name), nameof(MakerDto.MakerName))]
                public static partial MakerDto ToMakerDto(this Maker maker);
            }

            [DomainMapper]
            [UseStaticDomainMapper(typeof(OtherMapper))]
            public static partial class Mapper
            {
                public static partial IQueryable<CarDto> ProjectToCarDto(this IQueryable<Car> query);

                [MapperRequiredMapping(RequiredMappingStrategy.Target)]
                [MapProperty(nameof(Car.Name), nameof(CarDto.CarName))]
                [MapProperty(nameof(Car.Make), nameof(CarDto.Maker))]
                public static partial CarDto MapToCarDto(this Car car);
            }
            """
        );

        return TestHelper.VerifyGenerator(source);
    }

    [Fact]
    public void ProjectionWithUseStaticDomainMapperShouldReportDiagnosticWhenInliningFails()
    {
        var source = TestSourceBuilder.CSharp(
            """
            using DomainMap.Abstractions;
            using System.Linq;

            public class Maker
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public class Car
            {
                public int Id { get; set; }
                public string Name { get; set; } = null!;
                public Maker Make { get; set; } = null!;
            }

            public record MakerDto
            {
                public int Id { get; init; }
                public string MakerName { get; init; } = null!;
            }

            public record CarDto
            {
                public int Id { get; init; }
                public string CarName { get; init; } = null!;
                public MakerDto Maker { get; init; } = null!;
            }

            [DomainMapper]
            public static partial class OtherMapper
            {
                public static MakerDto ToMakerDto(Maker maker)
                {
                    var id = maker.Id;
                    return new MakerDto { Id = id, MakerName = maker.Name };
                }
            }

            [DomainMapper]
            [UseStaticDomainMapper(typeof(OtherMapper))]
            public static partial class Mapper
            {
                public static partial IQueryable<CarDto> ProjectToCarDto(this IQueryable<Car> query);

                [MapperRequiredMapping(RequiredMappingStrategy.Target)]
                [MapProperty(nameof(Car.Name), nameof(CarDto.CarName))]
                [MapProperty(nameof(Car.Make), nameof(CarDto.Maker))]
                public static partial CarDto MapToCarDto(this Car car);
            }
            """
        );

        TestHelper
            .GenerateMapper(source, TestHelperOptions.AllowDiagnostics)
            .Should()
            .HaveDiagnostic(DiagnosticDescriptors.QueryableProjectionMappingCannotInline);
    }
}
