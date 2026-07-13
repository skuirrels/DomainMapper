namespace DomainMap.IntegrationTests.Dto
{
    public interface ITestGenericValueDto<T>
    {
        T Value { get; set; }
    }
}
