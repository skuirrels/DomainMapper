using DomainMap.Abstractions;
using DomainMap.IntegrationTests.Dto;
using DomainMap.IntegrationTests.Models;

namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper]
    public partial class GenericUserMethodMapper
    {
        public partial DocumentDto MapDocument(Document source);

        private Optional<TTarget> MapOptional<TSource, TTarget>(Optional<TSource> source)
            where TSource : notnull
            where TTarget : notnull => source.HasValue ? Optional.Of(Map<TSource, TTarget>(source.Value)) : Optional.Empty<TTarget>();

        private partial TTarget Map<TSource, TTarget>(TSource source)
            where TSource : notnull
            where TTarget : notnull;

        private partial UserDto MapUser(User source);
    }
}
