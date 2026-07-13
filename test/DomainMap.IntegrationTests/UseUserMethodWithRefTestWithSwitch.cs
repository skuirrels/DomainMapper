using System.Threading.Tasks;
using DomainMap.IntegrationTests.Helpers;
using DomainMap.IntegrationTests.Mapper;
using DomainMap.IntegrationTests.Models;
using Shouldly;
using VerifyXunit;
using Xunit;

namespace DomainMap.IntegrationTests
{
    public class UseUserMethodWithRefTestWithSwitch : BaseMapperTest
    {
        [Fact]
        [VersionedSnapshot(Versions.NET8_0)]
        public Task SnapshotGeneratedSource()
        {
            var path = GetGeneratedMapperFilePath(nameof(UseUserMethodWithRefWithSwitch));
            return Verifier.VerifyFile(path);
        }

        [Fact]
        public void RunArrayMappingWithRefWithSwitch()
        {
            var modelTarget = new TestObjectProjectionTypeB { BaseValue = 5 };
            var modelSrc = new TestObjectProjectionTypeB { BaseValue = 6 };
            UseUserMethodWithRefWithSwitch.Merge(modelTarget, modelSrc);
            modelTarget.BaseValue.ShouldBe(11);
        }
    }
}
