using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainMapper.Benchmarks;

internal sealed record ComparisonCodeParityEntry(
    string Scenario,
    string RootMethod,
    bool Equivalent,
    string DomainMapperFingerprint,
    string MapperlyFingerprint
);

internal sealed record ComparisonCodeParityReport(IReadOnlyList<ComparisonCodeParityEntry> Scenarios)
{
    public IReadOnlySet<string> EquivalentScenarios =>
        Scenarios.Where(x => x.Equivalent).Select(x => x.Scenario).ToHashSet(StringComparer.Ordinal);
}

internal static class ComparisonCodeParity
{
    private const string DomainMapperClassName = "DomainMapperBenchmarkMapper";
    private const string MapperlyClassName = "MapperlyBenchmarkMapper";

    private static readonly IReadOnlyDictionary<string, string> _scenarioRoots = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Flat"] = "MapFlat",
        ["RenamedFlattened"] = "MapRenamed",
        ["NestedCollection"] = "MapOrder",
        ["ExistingTarget"] = "UpdateFlat",
        ["DomainFactory"] = "Place",
        ["ValueObjectFactory"] = "MapId",
    };

    public static ComparisonCodeParityReport Evaluate(
        string domainMapperGeneratedSource,
        string mapperlyGeneratedSource,
        string mapperDeclarationsSource
    )
    {
        var domainMapper = MapperModel.Create(DomainMapperClassName, domainMapperGeneratedSource, mapperDeclarationsSource);
        var mapperly = MapperModel.Create(MapperlyClassName, mapperlyGeneratedSource, mapperDeclarationsSource);
        var scenarios = _scenarioRoots
            .Where(x => domainMapper.HasMethod(x.Value) || mapperly.HasMethod(x.Value))
            .Select(x => BuildEntry(x.Key, x.Value, domainMapper, mapperly))
            .OrderBy(x => x.Scenario, StringComparer.Ordinal)
            .ToArray();
        return new ComparisonCodeParityReport(scenarios);
    }

    public static void Write(
        string domainMapperGeneratedPath,
        string mapperlyGeneratedPath,
        string mapperDeclarationsPath,
        string outputPath
    )
    {
        var report = Evaluate(
            File.ReadAllText(domainMapperGeneratedPath),
            File.ReadAllText(mapperlyGeneratedPath),
            File.ReadAllText(mapperDeclarationsPath)
        );
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static ComparisonCodeParityReport Read(string path) =>
        JsonSerializer.Deserialize<ComparisonCodeParityReport>(File.ReadAllText(path))
        ?? throw new InvalidOperationException($"Could not read comparison parity report {path}.");

    private static ComparisonCodeParityEntry BuildEntry(string scenario, string rootMethod, MapperModel domainMapper, MapperModel mapperly)
    {
        var domainMapperCanonical = domainMapper.Canonicalize(rootMethod);
        var mapperlyCanonical = mapperly.Canonicalize(rootMethod);
        var domainMapperFingerprint = Fingerprint(domainMapperCanonical);
        var mapperlyFingerprint = Fingerprint(mapperlyCanonical);
        return new ComparisonCodeParityEntry(
            scenario,
            rootMethod,
            string.Equals(domainMapperCanonical, mapperlyCanonical, StringComparison.Ordinal),
            domainMapperFingerprint,
            mapperlyFingerprint
        );
    }

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class MapperModel
    {
        private readonly IReadOnlyDictionary<string, MethodDeclarationSyntax> _methods;

        private MapperModel(IReadOnlyDictionary<string, MethodDeclarationSyntax> methods) => _methods = methods;

        public static MapperModel Create(string className, string generatedSource, string declarationsSource)
        {
            var methods = new Dictionary<string, MethodDeclarationSyntax>(StringComparer.Ordinal);
            AddMethods(methods, declarationsSource, className);
            AddMethods(methods, generatedSource, className);
            return new MapperModel(methods);
        }

        public bool HasMethod(string methodName) => _methods.ContainsKey(methodName);

        public string Canonicalize(string rootMethod)
        {
            if (!_methods.TryGetValue(rootMethod, out var root))
                throw new InvalidOperationException($"Mapping method {rootMethod} was not found.");

            var nonInlinedMethods = new HashSet<string>(StringComparer.Ordinal);
            var rewriter = new InliningRewriter(_methods, nonInlinedMethods, new HashSet<string>([rootMethod], StringComparer.Ordinal));
            var rootNode = CanonicalizeMethod(root, rewriter);
            var definitions = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var pending = new Queue<string>(nonInlinedMethods);
            while (pending.TryDequeue(out var methodName))
            {
                if (definitions.ContainsKey(methodName) || !_methods.TryGetValue(methodName, out var method))
                    continue;

                var dependencies = new HashSet<string>(StringComparer.Ordinal);
                var methodRewriter = new InliningRewriter(
                    _methods,
                    dependencies,
                    new HashSet<string>([rootMethod, methodName], StringComparer.Ordinal)
                );
                definitions[methodName] = CanonicalizeMethod(method, methodRewriter);
                foreach (var dependency in dependencies)
                {
                    pending.Enqueue(dependency);
                }
            }

            return string.Join("\n", [rootNode, .. definitions.Select(x => $"{x.Key}:{x.Value}")]);
        }

        private static string CanonicalizeMethod(MethodDeclarationSyntax method, InliningRewriter rewriter)
        {
            var significantAttributes = method
                .AttributeLists.SelectMany(x => x.Attributes)
                .Where(IsPerformanceAttribute)
                .Select(CanonicalizePerformanceAttribute)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (TryGetReturnedExpression(method, out var expression))
                return JoinAttributesAndBody(significantAttributes, RemoveRedundantParentheses(rewriter.Visit(expression)!));

            var normalizedMethod = method.WithAttributeLists(default).WithModifiers(default);
            return JoinAttributesAndBody(significantAttributes, RemoveRedundantParentheses(rewriter.Visit(normalizedMethod)!));
        }

        private static bool IsPerformanceAttribute(AttributeSyntax attribute)
        {
            var name = attribute.Name.ToString();
            return name.EndsWith("MethodImpl", StringComparison.Ordinal) || name.EndsWith("MethodImplAttribute", StringComparison.Ordinal);
        }

        private static string CanonicalizePerformanceAttribute(AttributeSyntax attribute) =>
            attribute
                .WithoutTrivia()
                .NormalizeWhitespace(eol: "\n")
                .ToFullString()
                .Replace("global::", string.Empty, StringComparison.Ordinal)
                .Replace("System.Runtime.CompilerServices.", string.Empty, StringComparison.Ordinal);

        private static string JoinAttributesAndBody(IReadOnlyCollection<string> attributes, string body) =>
            attributes.Count == 0 ? body : string.Join("\n", [.. attributes, body]);

        private static string RemoveRedundantParentheses(SyntaxNode node) =>
            new RedundantParenthesesRewriter()
                .Visit(node)!
                .NormalizeWhitespace(eol: "\n")
                .ToFullString()
                .Replace("global::DomainMapper.Benchmarks.", string.Empty, StringComparison.Ordinal);

        private static void AddMethods(IDictionary<string, MethodDeclarationSyntax> methods, string source, string className)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
            var mapperClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(x => x.Identifier.ValueText == className);
            foreach (var methodDeclaration in mapperClasses.SelectMany(x => x.Members.OfType<MethodDeclarationSyntax>()))
            {
                var method = methodDeclaration;
                var methodName = method.Identifier.ValueText;
                if (methods.TryGetValue(methodName, out var declaration))
                {
                    method = method.WithAttributeLists(declaration.AttributeLists.AddRange(method.AttributeLists));
                }

                if (method.Body != null || method.ExpressionBody != null || method.AttributeLists.Count > 0)
                    methods[methodName] = method;
            }
        }

        private static bool TryGetReturnedExpression(MethodDeclarationSyntax method, out ExpressionSyntax expression)
        {
            if (method.ExpressionBody != null)
            {
                expression = method.ExpressionBody.Expression;
                return true;
            }

            if (method.Body?.Statements is [ReturnStatementSyntax { Expression: { } returnedExpression }])
            {
                expression = returnedExpression;
                return true;
            }

            if (
                method.Body?.Statements
                    is [
                        LocalDeclarationStatementSyntax
                        {
                            Declaration:
                            { Variables: [{ Identifier.ValueText: var variableName, Initializer.Value: { } initializedExpression }] },
                        },
                        ReturnStatementSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: var returnedVariable } },
                    ]
                && variableName == returnedVariable
            )
            {
                expression = initializedExpression;
                return true;
            }

            expression = null!;
            return false;
        }

        private sealed class InliningRewriter(
            IReadOnlyDictionary<string, MethodDeclarationSyntax> methods,
            ISet<string> nonInlinedMethods,
            IReadOnlySet<string> callStack
        ) : CSharpSyntaxRewriter
        {
            public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var visitedArguments = node
                    .ArgumentList.Arguments.Select(x => x.WithExpression((ExpressionSyntax)Visit(x.Expression)!))
                    .ToArray();
                var visitedNode = node.WithArgumentList(node.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(visitedArguments)));
                if (visitedNode.Expression is not IdentifierNameSyntax identifier)
                    return base.VisitInvocationExpression(visitedNode);

                var methodName = identifier.Identifier.ValueText;
                if (!methods.TryGetValue(methodName, out var method) || callStack.Contains(methodName))
                    return base.VisitInvocationExpression(visitedNode);

                if (
                    !TryGetReturnedExpression(method, out var returnedExpression)
                    || method.ParameterList.Parameters.Count != visitedArguments.Length
                )
                {
                    nonInlinedMethods.Add(methodName);
                    return base.VisitInvocationExpression(visitedNode);
                }

                var substitutions = method
                    .ParameterList.Parameters.Select(
                        (parameter, index) => (parameter.Identifier.ValueText, visitedArguments[index].Expression)
                    )
                    .ToDictionary(x => x.ValueText, x => x.Expression, StringComparer.Ordinal);
                var substitutedExpression = (ExpressionSyntax)new ParameterSubstitutionRewriter(substitutions).Visit(returnedExpression)!;
                var nestedStack = callStack.Append(methodName).ToHashSet(StringComparer.Ordinal);
                var nestedRewriter = new InliningRewriter(methods, nonInlinedMethods, nestedStack);
                return SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)nestedRewriter.Visit(substitutedExpression)!);
            }
        }

        private sealed class ParameterSubstitutionRewriter(IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
            : CSharpSyntaxRewriter
        {
            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
                substitutions.TryGetValue(node.Identifier.ValueText, out var replacement)
                    ? SyntaxFactory.ParenthesizedExpression(replacement.WithoutTrivia())
                    : base.VisitIdentifierName(node);
        }

        private sealed class RedundantParenthesesRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
            {
                var expression = (ExpressionSyntax)Visit(node.Expression)!;
                return
                    expression
                        is IdentifierNameSyntax
                            or MemberAccessExpressionSyntax
                            or InvocationExpressionSyntax
                            or ObjectCreationExpressionSyntax
                            or ElementAccessExpressionSyntax
                            or LiteralExpressionSyntax
                    ? expression.WithTriviaFrom(node)
                    : node.WithExpression(expression);
            }
        }
    }
}
