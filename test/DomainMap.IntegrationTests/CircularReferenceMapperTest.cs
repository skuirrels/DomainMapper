using System.Threading.Tasks;
using DomainMap.IntegrationTests.Helpers;
using DomainMap.IntegrationTests.Mapper;
using DomainMap.IntegrationTests.Models;
using Shouldly;
using VerifyXunit;
using Xunit;

namespace DomainMap.IntegrationTests
{
    public class CircularReferenceMapperTest : BaseMapperTest
    {
        [Fact]
        public void ShouldMapCircularReference()
        {
            var obj = new CircularReferenceObject
            {
                Value = 1,
                Parent = new() { Value = 2 },
            };
            obj.Parent.Parent = obj;

            var dto = CircularReferenceMapper.ToDto(obj);
            dto.Value.ShouldBe(1);
            dto.Parent.ShouldNotBeNull();
            dto.Parent!.Value.ShouldBe(2);
            dto.Parent.Parent.ShouldBe(dto);
        }

        [Fact]
        [VersionedSnapshot(Versions.NET8_0)]
        public Task SnapshotGeneratedSource()
        {
            var path = GetGeneratedMapperFilePath(nameof(CircularReferenceMapper));
            return Verifier.VerifyFile(path);
        }
    }
}
