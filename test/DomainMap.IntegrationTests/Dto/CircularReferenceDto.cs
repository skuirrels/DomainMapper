namespace DomainMap.IntegrationTests.Dto
{
    public class CircularReferenceDto
    {
        public int Value { get; set; }

        public CircularReferenceDto? Parent { get; set; }
    }
}
