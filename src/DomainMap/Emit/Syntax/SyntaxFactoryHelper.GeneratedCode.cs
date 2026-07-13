using DomainMap.Configuration;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Emit.Syntax;

public partial struct SyntaxFactoryHelper
{
    public AttributeListSyntax GeneratedCodeAttribute()
    {
        return Attribute(
            DomainMapGeneratedCodeAttribute.GeneratedCodeAttributeName,
            StringLiteral(DomainMapGeneratedCodeAttribute.GeneratorToolName),
            StringLiteral(DomainMapGeneratedCodeAttribute.GeneratorToolVersion)
        );
    }
}
