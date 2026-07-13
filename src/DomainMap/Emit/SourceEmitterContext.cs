using DomainMap.Emit.Syntax;
using DomainMap.Helpers;

namespace DomainMap.Emit;

public record SourceEmitterContext(bool IsStatic, UniqueNameBuilder NameBuilder, SyntaxFactoryHelper SyntaxFactory)
{
    public SourceEmitterContext AddIndentation() => this with { SyntaxFactory = SyntaxFactory.AddIndentation() };

    public SourceEmitterContext RemoveIndentation() => this with { SyntaxFactory = SyntaxFactory.RemoveIndentation() };
}
