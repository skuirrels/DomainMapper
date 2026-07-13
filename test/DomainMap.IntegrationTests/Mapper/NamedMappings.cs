using DomainMap.Abstractions;
using DomainMap.IntegrationTests.Dto;
using DomainMap.IntegrationTests.Models;

namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper(AutoUserMappings = false)]
    public static partial class NamedMappings
    {
        [MapValue(nameof(NamedMappingValuesDto.FromMapValue), Use = "CustomStringValueBuilder")]
        [MapProperty(nameof(NamedMappingObject.SourceValue), nameof(NamedMappingValuesDto.FromMapPropertyUse), Use = "CustomModifyValue")]
        [MapPropertyFromSource(nameof(NamedMappingValuesDto.FromMapPropertyFromSource), Use = "CustomUseSource")]
        [NamedMapping("CustomMappingName")]
        public static partial NamedMappingValuesDto MapWithNamedMappings(NamedMappingObject source);

        [IncludeMappingConfiguration("CustomMappingName")]
        public static partial void UpdateDto(NamedMappingObject source, NamedMappingValuesDto target);

        [NamedMapping("CustomStringValueBuilder")]
        private static string StringValueBuilder() => "fooBar";

        [NamedMapping("CustomModifyValue")]
        private static string ModifyValue(string text) => text + "-modified";

        [NamedMapping("CustomUseSource")]
        private static string UseSource(NamedMappingObject source) => source.SourceValue + "-from-source";
    }
}
