using DomainMap.Abstractions;

// this is tested with EnumMapper in MapperDefaultsTest
[assembly: MapperDefaults(EnumMappingStrategy = EnumMappingStrategy.ByName)]
