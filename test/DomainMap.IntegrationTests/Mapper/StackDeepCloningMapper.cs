using System.Collections.Generic;
using DomainMap.Abstractions;

namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper(UseDeepCloning = true)]
    public static partial class StackDeepCloningMapper
    {
        public static partial Stack<int> Copy(Stack<int> src);
    }

    [DomainMapper(UseDeepCloning = true, StackCloningStrategy = StackCloningStrategy.ReverseOrder)]
    public static partial class StackDeepCloningLegacyMapper
    {
        public static partial Stack<int> Copy(Stack<int> src);
    }
}
