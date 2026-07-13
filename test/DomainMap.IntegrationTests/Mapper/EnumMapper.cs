using DomainMap.Abstractions;

// is used to test mapper defaults
namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper]
    public static partial class EnumMapper
    {
        public static partial Enum2 Map(Enum1 e);
    }

    public enum Enum1
    {
        Value1 = 4,
        Value2 = 7,
    }

    public enum Enum2
    {
        Value1,
        Value2,
    }
}
