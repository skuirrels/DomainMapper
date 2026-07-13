using DomainMap.Emit;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.UnsafeAccess;

public interface IUnsafeAccessors
{
    int Count { get; }

    IEnumerable<MemberDeclarationSyntax> Build(SourceEmitterContext ctx, CancellationToken cancellationToken);
}
