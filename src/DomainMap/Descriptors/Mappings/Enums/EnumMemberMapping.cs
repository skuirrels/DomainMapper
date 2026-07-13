using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DomainMap.Emit.Syntax.SyntaxFactoryHelper;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DomainMap.Descriptors.Mappings.Enums;

public sealed record EnumMemberMapping(ExpressionSyntax SourceSyntax, ExpressionSyntax TargetSyntax)
{
    public SwitchExpressionArmSyntax BuildSwitchArm() => SwitchArm(ConstantPattern(SourceSyntax), TargetSyntax);
}
