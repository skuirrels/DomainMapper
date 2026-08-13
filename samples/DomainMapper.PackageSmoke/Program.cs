using DomainMapper.Abstractions;

var target = PackageSmokeMapper.Map(new PackageSmokeSource { Id = 42, Name = "DomainMapper" });
if (target.Id != 42 || target.Name != "DomainMapper")
    throw new InvalidOperationException("DomainMapper generated an invalid package smoke mapping.");

Console.WriteLine("DomainMapper package smoke test passed.");

[DomainMapper]
public static partial class PackageSmokeMapper
{
    public static partial PackageSmokeTarget Map(PackageSmokeSource source);
}

public sealed class PackageSmokeSource
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class PackageSmokeTarget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
