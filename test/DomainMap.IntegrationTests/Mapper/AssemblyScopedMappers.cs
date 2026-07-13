using DomainMap.Abstractions;
using DomainMap.IntegrationTests.Dto;
using DomainMap.IntegrationTests.Mapper;
using DomainMap.IntegrationTests.Models;

[assembly: UseStaticDomainMapper(typeof(AssemblyScopedMappers))]

namespace DomainMap.IntegrationTests.Mapper
{
    public static class AssemblyScopedMappers
    {
        public static AssemblyScopedDto ToDto(AssemblyScopedModel obj) => new AssemblyScopedDto() { Value = obj.Value + 1 };
    }
}
