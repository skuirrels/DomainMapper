namespace DomainMap.IntegrationTests.Models
{
    public interface ITestGenericValue<T>
    {
        T Value { get; set; }
    }
}
