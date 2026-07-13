#if NET8_0_OR_GREATER
using System.Threading.Tasks;
using DomainMap.IntegrationTests.Mapper;
using Shouldly;
using VerifyXunit;
using Xunit;

namespace DomainMap.IntegrationTests
{
    public class NestedMapperPrimaryConstructorTest : BaseMapperTest
    {
        [Fact]
        public Task SnapshotGeneratedSource()
        {
            var path = GetGeneratedMapperFilePath(
                $"{nameof(NestedTestMapperPrimaryConstructor)}.{nameof(NestedTestMapper.TestNesting)}.{nameof(NestedTestMapper.TestNesting.NestedMapper)}"
            );
            return Verifier.VerifyFile(path);
        }

        [Fact]
        public void RunMappingShouldWork()
        {
            var v = NestedTestMapperPrimaryConstructor.TestNesting.NestedMapper.ToDecimal(10);
            v.ShouldBe(10.00m);
        }
    }
}
#endif
