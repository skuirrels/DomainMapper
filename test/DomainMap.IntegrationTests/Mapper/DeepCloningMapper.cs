using DomainMap.Abstractions;
using DomainMap.IntegrationTests.Models;

namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper(UseDeepCloning = true)]
    public static partial class DeepCloningMapper
    {
        public static partial IdObject Copy(IdObject src);

        [MapperIgnoreSource(nameof(TestObject.IgnoredIntValue))]
        [MapperIgnoreSource(nameof(TestObject.IgnoredStringValue))]
        [MapperIgnoreSource(nameof(TestObject.ImmutableHashSetValue))]
        [MapperIgnoreSource(nameof(TestObject.SpanValue))]
        [MapperIgnoreObsoleteMembers]
        public static partial TestObject Copy(TestObject src);
    }
}
