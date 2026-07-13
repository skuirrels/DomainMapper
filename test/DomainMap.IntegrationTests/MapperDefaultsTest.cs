using System.Threading.Tasks;
using DomainMap.IntegrationTests.Helpers;
using DomainMap.IntegrationTests.Mapper;
using Shouldly;
using VerifyXunit;
using Xunit;

namespace DomainMap.IntegrationTests
{
    public class MapperDefaultsTest : BaseMapperTest
    {
        [Fact]
        [VersionedSnapshot(Versions.NET8_0)]
        public Task SnapshotGeneratedSource()
        {
            var path = GetGeneratedMapperFilePath(nameof(EnumMapper));
            return Verifier.VerifyFile(path);
        }

        [Fact]
        public void RunMappingShouldWork()
        {
            var enum2 = EnumMapper.Map(Enum1.Value1);
            enum2.ShouldBe(Enum2.Value1);
        }
    }
}
