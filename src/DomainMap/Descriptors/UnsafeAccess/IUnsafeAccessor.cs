using DomainMap.Emit;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMap.Descriptors.UnsafeAccess;

/// <summary>
/// Represents a method accessor for inaccessible members.
/// This uses the .NET 8.0 UnsafeAccessorAttribute to access members that are not public or visible without the use of reflection.
/// <see href="https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.unsafeaccessorattribute">See here</see>
/// </summary>
public interface IUnsafeAccessor
{
    MethodDeclarationSyntax BuildAccessorMethod(SourceEmitterContext ctx);
}
