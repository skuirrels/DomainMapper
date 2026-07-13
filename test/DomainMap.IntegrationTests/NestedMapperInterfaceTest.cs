using System.Threading.Tasks;
using DomainMap.IntegrationTests.Helpers;
using DomainMap.IntegrationTests.Mapper;
using Shouldly;
using VerifyXunit;
using Xunit;

namespace DomainMap.IntegrationTests
{
    public class NestedMapperInterfaceTest : BaseMapperTest
    {
        [Fact]
        [VersionedSnapshot(Versions.NET8_0)]
        public Task SnapshotGeneratedSource()
        {
            var path = GetGeneratedMapperFilePath($"{nameof(INestedTestMapper)}.{nameof(NestedTestMapper.TestNesting.NestedMapper)}");
            return Verifier.VerifyFile(path);
        }

        [Fact]
        public void RunMappingShouldWork()
        {
            var v = INestedTestMapper.NestedMapper.ToDecimal(10);
            v.ShouldBe(10.00m);
        }
    }
}
