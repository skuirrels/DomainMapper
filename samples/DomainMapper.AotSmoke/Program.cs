using DomainMapper.Abstractions;

var source = new Source { Value = 42 };
source.Next = source;
var target = Mapper.Map(source);
var runtime = (Target)Mapper.MapRuntime(source, typeof(Target))!;
return ReferenceEquals(target, target.Next) && ReferenceEquals(runtime, runtime.Next) ? 0 : 1;

public sealed class Source
{
    public int Value { get; set; }

    public Source? Next { get; set; }
}

public sealed class Target
{
    public int Value { get; set; }

    public Target? Next { get; set; }
}

[DomainMapper]
[MapRegistry]
public static partial class Mapper
{
    [MapReferenceTracking]
    public static partial Target Map(Source source);
}
