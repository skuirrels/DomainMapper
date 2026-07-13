#if NET8_0_OR_GREATER
using DomainMap.Abstractions;

namespace DomainMap.IntegrationTests.Mapper
{
    public partial class NestedTestMapperPrimaryConstructor(string test)
    {
        private readonly string _test = test;

        public partial class TestNesting
        {
            [DomainMapper]
            public static partial class NestedMapper
            {
                public static partial decimal ToDecimal(int value);
            }
        }
    }
}
#endif
