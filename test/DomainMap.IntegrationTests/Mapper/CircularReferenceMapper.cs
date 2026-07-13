using DomainMap.Abstractions;
using DomainMap.IntegrationTests.Dto;
using DomainMap.IntegrationTests.Models;

namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper(UseReferenceHandling = true)]
    public static partial class CircularReferenceMapper
    {
        public static partial CircularReferenceDto ToDto(CircularReferenceObject obj);
    }
}
