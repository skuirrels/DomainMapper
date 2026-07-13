using DomainMap.Abstractions;

namespace DomainMap.IntegrationTests.Mapper
{
    [DomainMapper]
    public partial class BaseMapper
    {
        public virtual partial long IntToLong(int value);

        public partial string IntToString(int value);
    }

    [DomainMapper]
    public partial class DerivedMapper : BaseMapper
    {
        public override partial long IntToLong(int value);
    }

    [DomainMapper]
    public partial class DerivedMapper2 : BaseMapper
    {
        public sealed override partial long IntToLong(int value);

        public new partial string IntToString(int value);
    }
}
