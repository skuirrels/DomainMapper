using System.Linq;
using DomainMap.Abstractions;
using DomainMap.IntegrationTests.Dto;
using DomainMap.IntegrationTests.Models;

namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper]
    public static partial class AdditionalParameterInliningMapper
    {
        private static partial AdditionalParametersDto MapToDto(IdObject source, int valueFromParameter);

        public static partial IQueryable<AdditionalParametersDto> ProjectWithAdditionalParameter(
            this IQueryable<IdObject> q,
            int valueFromParameter
        );
    }
}
