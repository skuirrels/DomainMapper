namespace DomainMapper.Engine;

internal sealed class MappingContract
{
    public MappingContract(string methodName, string declaration, string body, MappingShape shape)
    {
        MethodName = methodName;
        Declaration = declaration;
        Body = body;
        Shape = shape;
    }

    public string MethodName { get; }

    public string Declaration { get; }

    public string Body { get; }

    public MappingShape Shape { get; }
}
