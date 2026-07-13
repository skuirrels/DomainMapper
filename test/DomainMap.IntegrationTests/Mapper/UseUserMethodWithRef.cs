using System.Linq;
using DomainMap.Abstractions;
using DomainMap.IntegrationTests.Models;

namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper(AllowNullPropertyAssignment = false, UseDeepCloning = true, RequiredMappingStrategy = RequiredMappingStrategy.Source)]
    public static partial class UseUserMethodWithRef
    {
        [MapProperty(nameof(ArrayObject.IntArray), nameof(ArrayObject.IntArray), Use = nameof(MapArray))] // Use is required otherwise it will generate it's own
        public static partial void Merge([MappingTarget] ArrayObject target, ArrayObject second);

        private static void MapArray([MappingTarget] ref int[] target, int[] second) => target = target.Concat(second).ToArray();
    }
}
