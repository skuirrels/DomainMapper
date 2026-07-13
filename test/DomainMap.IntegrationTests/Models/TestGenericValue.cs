namespace DomainMap.IntegrationTests.Models
{
    public class TestGenericValue : ITestGenericValue<float>
    {
        public float Value { get; set; }
    }
}
