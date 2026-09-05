namespace DomainMapper.Engine;

/// <summary>A planned target construction: the creation expression plus any statements that must follow it.</summary>
internal sealed class CreationPlan
{
    public CreationPlan(string expression, string assignments = "")
    {
        Expression = expression;
        Assignments = assignments;
    }

    public string Expression { get; }

    public string Assignments { get; }

    public static CreationPlan? FromExpression(string? expression) => expression == null ? null : new CreationPlan(expression);

    public string ToTargetStatements() =>
        Assignments.Length == 0 ? $"var target = {Expression};" : $"var target = {Expression};\n{Assignments}";
}
