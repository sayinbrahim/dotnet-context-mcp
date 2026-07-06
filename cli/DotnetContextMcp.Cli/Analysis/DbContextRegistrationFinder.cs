using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetContextMcp.Cli.Analysis;

public record DbContextRegistration(
    string DbContextName,
    string RegistrationMethod,     // "AddDbContext" | "AddDbContextPool" | "AddDbContextFactory"
    string FilePath,
    int LineNumber,
    string ProjectName,
    string Lifetime,               // "Scoped" | "Singleton" | "Transient"
    bool HasConnectionString,
    string? ConnectionStringSource,  // "Configuration" | "Hardcoded" | "EnvironmentVariable" | null
    string? Provider,              // "SqlServer" | "Npgsql" | "Sqlite" | "InMemory" | "Oracle" | null
    string? ProviderMethod         // "UseSqlServer" | "UseNpgsql" | etc.
);

public record RegistrationStats(
    int TotalRegistrations,
    Dictionary<string, int> ByMethod,
    Dictionary<string, int> ByProvider
);

public class DbContextRegistrationFinder
{
    private static readonly HashSet<string> RegistrationMethodNames = new()
    {
        "AddDbContext", "AddDbContextPool", "AddDbContextFactory"
    };

    public (List<DbContextRegistration>, RegistrationStats) Find(Solution solution)
    {
        return FindAsync(solution).GetAwaiter().GetResult();
    }

    private static async Task<(List<DbContextRegistration>, RegistrationStats)> FindAsync(Solution solution)
    {
        var results = new List<DbContextRegistration>();
        var solutionDirectory = string.IsNullOrEmpty(solution.FilePath)
            ? string.Empty
            : Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var syntaxTree = await document.GetSyntaxTreeAsync();
                if (syntaxTree is null) continue;

                var root = await syntaxTree.GetRootAsync();

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;
                    if (memberAccess.Name is not GenericNameSyntax generic) continue;

                    var methodName = generic.Identifier.Text;
                    if (!RegistrationMethodNames.Contains(methodName)) continue;

                    var dbContextName = generic.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
                    if (dbContextName is null) continue;

                    var lineNumber = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var fullPath = syntaxTree.FilePath;
                    var relativePath = GetRelativePath(solutionDirectory, fullPath);

                    var configureLambda = invocation.ArgumentList.Arguments
                        .Select(a => a.Expression)
                        .OfType<LambdaExpressionSyntax>()
                        .FirstOrDefault();

                    var useInvocation = configureLambda is null ? null : FindUseInvocation(configureLambda.Body);
                    var providerMethod = useInvocation?.Expression is MemberAccessExpressionSyntax useMember
                        ? useMember.Name.Identifier.Text
                        : null;
                    var provider = InferProvider(providerMethod);

                    var (hasConnectionString, connectionStringSource) = AnalyzeConnectionString(useInvocation);

                    var lifetime = methodName switch
                    {
                        "AddDbContext" => "Scoped",
                        "AddDbContextPool" => "Scoped",
                        "AddDbContextFactory" => "Singleton",
                        _ => "Scoped"
                    };

                    results.Add(new DbContextRegistration(
                        DbContextName: dbContextName,
                        RegistrationMethod: methodName,
                        FilePath: relativePath,
                        LineNumber: lineNumber,
                        ProjectName: project.Name,
                        Lifetime: lifetime,
                        HasConnectionString: hasConnectionString,
                        ConnectionStringSource: connectionStringSource,
                        Provider: provider,
                        ProviderMethod: providerMethod
                    ));
                }
            }
        }

        var stats = new RegistrationStats(
            TotalRegistrations: results.Count,
            ByMethod: results
                .GroupBy(r => r.RegistrationMethod)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByProvider: results
                .Where(r => r.Provider is not null)
                .GroupBy(r => r.Provider!)
                .ToDictionary(g => g.Key, g => g.Count())
        );

        return (results, stats);
    }

    // ── Provider detection (options.Use<Provider>(...) inside the configure lambda) ─

    private static InvocationExpressionSyntax? FindUseInvocation(SyntaxNode lambdaBody)
    {
        return lambdaBody.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv =>
                inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.Text.StartsWith("Use", StringComparison.Ordinal));
    }

    private static string? InferProvider(string? providerMethod) => providerMethod switch
    {
        "UseSqlServer" => "SqlServer",
        "UseNpgsql" => "Npgsql",
        "UseSqlite" => "Sqlite",
        "UseInMemoryDatabase" => "InMemory",
        "UseOracle" => "Oracle",
        "UseMySql" => "MySQL",
        "UseCosmos" => "Cosmos",
        _ => null
    };

    // ── Connection string source detection ──────────────────────────────────

    private static (bool HasConnectionString, string? Source) AnalyzeConnectionString(InvocationExpressionSyntax? useInvocation)
    {
        if (useInvocation is null || useInvocation.ArgumentList.Arguments.Count == 0)
            return (false, null);

        var argExpr = useInvocation.ArgumentList.Arguments[0].Expression;

        switch (argExpr)
        {
            case InvocationExpressionSyntax argInvocation:
                var calleeName = (argInvocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
                return calleeName switch
                {
                    "GetConnectionString" => (true, "Configuration"),
                    "GetEnvironmentVariable" => (true, "EnvironmentVariable"),
                    _ => (true, null)
                };

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                return (true, "Hardcoded");

            case IdentifierNameSyntax:
                return (true, null);

            default:
                return (true, null);
        }
    }

    private static string GetRelativePath(string basePath, string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return string.Empty;
        if (string.IsNullOrEmpty(basePath)) return fullPath;
        try
        {
            return Path.GetRelativePath(basePath, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }
}
