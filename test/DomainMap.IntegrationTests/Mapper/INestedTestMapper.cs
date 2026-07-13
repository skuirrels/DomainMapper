using DomainMap.Abstractions;

namespace DomainMap.IntegrationTests.Mapper
{
    public partial interface INestedTestMapper
    {
        [DomainMapper]
        public static partial class NestedMapper
        {
            public static partial decimal ToDecimal(int value);
        }
    }
}
