using System.Threading.Tasks;
using DomainMap.IntegrationTests.Helpers;
using DomainMap.IntegrationTests.Mapper;
using DomainMap.IntegrationTests.Models;
using Shouldly;
using VerifyXunit;
using Xunit;

namespace DomainMap.IntegrationTests
{
    public class DeepCloningMapperTest : BaseMapperTest
    {
        [Fact]
        [VersionedSnapshot(Versions.NET8_0)]
        public Task SnapshotGeneratedSource()
        {
            var path = GetGeneratedMapperFilePath(nameof(DeepCloningMapper));
            return Verifier.VerifyFile(path);
        }

        [Fact]
        [VersionedSnapshot(Versions.NET8_0 | Versions.NET9_0)]
        public Task RunMappingShouldWork()
        {
            var model = NewTestObj();
            var dto = DeepCloningMapper.Copy(model);
            return Verifier.Verify(dto);
        }

        [Fact]
        public void RunIdMappingShouldWork()
        {
            var source = new IdObject { IdValue = 20 };
            var copy = DeepCloningMapper.Copy(source);
            source.ShouldNotBeSameAs(copy);
            copy.IdValue.ShouldBe(20);
        }
    }
}
