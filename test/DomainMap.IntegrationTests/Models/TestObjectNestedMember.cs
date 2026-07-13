namespace DomainMap.IntegrationTests.Models
{
    public class TestObjectNestedMember
    {
        public int NestedMemberId { get; set; }
        public TestObjectNested? NestedMemberObject { get; set; }
    }
}
