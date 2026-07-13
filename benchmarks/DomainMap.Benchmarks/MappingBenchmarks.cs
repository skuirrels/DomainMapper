using BenchmarkDotNet.Attributes;
using DomainMap.IntegrationTests;
using DomainMap.IntegrationTests.Dto;
using DomainMap.IntegrationTests.Mapper;
using DomainMap.IntegrationTests.Models;

namespace DomainMap.Benchmarks;

[ArtifactsPath("artifacts")]
[MemoryDiagnoser]
[InProcess]
public class MappingBenchmarks
{
    private readonly TestObject _testObject;
    private readonly TestObjectDto _testObjectDto;
    private readonly IdObject _idObject;

    public MappingBenchmarks()
    {
        _testObject = BaseMapperTest.NewTestObj();
        _testObjectDto = StaticTestMapper.MapToDto(_testObject);
        _idObject = new IdObject { IdValue = 143 };
    }

    [Benchmark(Description = "MapComplexToDto")]
    public TestObjectDto MapComplexToDto() => StaticTestMapper.MapToDto(_testObject);

    [Benchmark(Description = "MapComplexToExistingDto")]
    public void MapComplexToExistingDto() => StaticTestMapper.UpdateDto(_testObject, _testObjectDto);

    [Benchmark(Description = "MapSimpleDeepClone")]
    public IdObject MapSimpleDeepClone() => DeepCloningMapper.Copy(_idObject);

    [Benchmark(Description = "MapComplexDeepClone")]
    public TestObject MapComplexDeepClone() => DeepCloningMapper.Copy(_testObject);
}
