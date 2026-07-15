using DomainMap.Configuration;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Emit.Syntax;

public partial struct SyntaxFactoryHelper
{
    public AttributeListSyntax GeneratedCodeAttribute() =>
        Indentation < CachedAttributeIndentationCount
            ? AttributeCache.GeneratedCode[Indentation]
            : BuildGeneratedCodeAttribute(Indentation);

    private static AttributeListSyntax BuildGeneratedCodeAttribute(int indentation)
    {
        var syntaxFactory = new SyntaxFactoryHelper(indentation, default);
        return syntaxFactory.Attribute(
            DomainMapGeneratedCodeAttribute.GeneratedCodeAttributeName,
            StringLiteral(DomainMapGeneratedCodeAttribute.GeneratorToolName),
            StringLiteral(DomainMapGeneratedCodeAttribute.GeneratorToolVersion)
        );
    }
}
