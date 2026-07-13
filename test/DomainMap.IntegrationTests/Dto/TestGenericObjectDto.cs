using DomainMap.Abstractions;

namespace DomainMap.IntegrationTests.Dto
{
    public class TestGenericObjectDto<T, TValue>
        where T : struct
        where TValue : ITestGenericValueDto<float>
    {
        [MapperIgnore]
        public T ExposedId => Id;

        private T Id { get; set; }

        [MapperIgnore]
        public TValue ExposedValue => Value;

        private TValue Value { get; set; } = default!;

        public static TestGenericObjectDto<int, TestGenericValueDto> SampleValue =>
            new()
            {
                Id = 1,
                Value = new() { Value = 1.5f },
            };
    }
}
