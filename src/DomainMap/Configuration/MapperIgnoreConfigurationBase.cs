namespace DomainMap.Configuration;

public abstract record MapperIgnoreConfigurationBase : HasSyntaxReference
{
    public string? Justification { get; set; }
}
