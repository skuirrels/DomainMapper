using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Symbols;

public sealed record MapperDeclaration(INamedTypeSymbol Symbol, ClassDeclarationSyntax Syntax);
