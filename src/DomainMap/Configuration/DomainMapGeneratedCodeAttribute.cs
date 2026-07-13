using System.Reflection;
using DomainMap.Emit.Syntax;

namespace DomainMap.Configuration;

internal static class DomainMapGeneratedCodeAttribute
{
    public const string GeneratedCodeAttributeName = "global::System.CodeDom.Compiler.GeneratedCode";

    private static readonly AssemblyName _generatorAssemblyName = typeof(SyntaxFactoryHelper).Assembly.GetName();

    public static readonly string GeneratorToolName = _generatorAssemblyName.Name;
    public static readonly string GeneratorToolVersion = _generatorAssemblyName.Version.ToString();
}
