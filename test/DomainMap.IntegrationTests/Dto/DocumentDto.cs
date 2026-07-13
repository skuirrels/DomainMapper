using DomainMap.IntegrationTests.Models;

namespace DomainMap.IntegrationTests.Dto
{
    public record DocumentDto(string Title, UserDto CreatedBy, Optional<UserDto> ModifiedBy);

    public record UserDto(string Name);
}
