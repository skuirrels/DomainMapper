using DomainMap.Abstractions;

namespace DomainMap.IntegrationTests.Mapper
{
    public static partial class NestedTestMapper
    {
        public static partial class TestNesting
        {
            [DomainMapper]
            public static partial class NestedMapper
            {
                public static partial decimal ToDecimal(int value);
            }
        }
    }
}
