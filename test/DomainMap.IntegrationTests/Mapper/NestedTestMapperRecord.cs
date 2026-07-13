using DomainMap.Abstractions;

namespace DomainMap.IntegrationTests.Mapper
{
    public partial record NestedTestMapperRecord(string Result)
    {
        public partial record TestNesting
        {
            [DomainMapper]
            public static partial class NestedMapper
            {
                public static partial decimal ToDecimal(int value);
            }
        }
    }
}
