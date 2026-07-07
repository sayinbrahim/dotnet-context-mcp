using Microsoft.CodeAnalysis;

namespace DotnetContextMcp.Cli.Analysis;

public record SolutionHealthReport(
    string SolutionPath,
    HealthSummary Summary,
    List<HealthIssue> Issues,
    List<string> Recommendations,
    List<DbContextHealth> ByDbContext
);

public record HealthSummary(
    int TotalDbContexts,
    int TotalEntities,
    int TotalMigrations,
    int TotalRegistrations,
    int TotalRelationships,
    int HealthScore,           // 0-100
    string Grade               // A/B/C/D/F, mapped from score
);

public record HealthIssue(
    string Severity,           // "info" | "warning" | "error"
    string Category,           // "multi-context-registration" | "hardcoded-connection-string" | etc.
    string Message,
    List<string> AffectedItems // ["file.cs:line", ...]
);

public record DbContextHealth(
    string Name,
    int Entities,
    int Migrations,
    int Relationships,
    int Registrations
);

public class SolutionHealthAnalyzer
{
    private readonly RelationshipFinder _relationshipFinder;
    private readonly DbContextRegistrationFinder _registrationFinder;

    public SolutionHealthAnalyzer()
    {
        _relationshipFinder = new RelationshipFinder();
        _registrationFinder = new DbContextRegistrationFinder();
    }

    public async Task<SolutionHealthReport> AnalyzeAsync(Solution solution, string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? string.Empty;

        var (rawDbContexts, _, _) = await DbContextFinder.FindAsync(solution, solutionDirectory);
        var (registrations, _) = _registrationFinder.Find(solution);

        // DbContextFinder scans per-project compilations; a DbContext defined in a class library
        // referenced by another project (e.g. Data + Web) shows up once per referencing project.
        // Dedup by fully-qualified name so it isn't treated as multiple distinct DbContexts.
        var dbContexts = rawDbContexts
            .GroupBy(x => $"{x.Namespace}.{x.Name}")
            .Select(g => g.First())
            .ToList();

        var byDbContext = new List<DbContextHealth>();
        var issues = new List<HealthIssue>();

        foreach (var ctx in dbContexts)
        {
            var (entities, _, _) = await EntityFinder.FindAsync(solution, solutionDirectory, ctx.Name);
            var (migrations, _, _) = await MigrationFinder.FindAsync(solution, solutionDirectory, ctx.Name);
            var relationships = _relationshipFinder.Find(solution, ctx.Name);
            var ctxRegistrations = registrations.Where(r => r.DbContextName == ctx.Name).ToList();

            byDbContext.Add(new DbContextHealth(
                ctx.Name,
                entities.Count,
                migrations.Count,
                relationships.Count,
                ctxRegistrations.Count
            ));

            issues.AddRange(DetectIssues(ctx, entities, migrations, relationships, ctxRegistrations));
        }

        var healthScore = ComputeHealthScore(issues);

        var summary = new HealthSummary(
            dbContexts.Count,
            byDbContext.Sum(x => x.Entities),
            byDbContext.Sum(x => x.Migrations),
            registrations.Count,
            byDbContext.Sum(x => x.Relationships),
            healthScore,
            ScoreToGrade(healthScore)
        );

        var recommendations = BuildRecommendations(issues);

        return new SolutionHealthReport(
            solutionPath,
            summary,
            issues,
            recommendations,
            byDbContext
        );
    }

    private static List<HealthIssue> DetectIssues(
        DbContextInfo ctx,
        List<EntityInfo> entities,
        List<MigrationInfo> migrations,
        List<RelationshipInfo> relationships,
        List<DbContextRegistration> ctxRegistrations)
    {
        var issues = new List<HealthIssue>();

        // Category 1 — multi-context-registration
        if (ctxRegistrations.Count > 1)
        {
            var methods = ctxRegistrations.Select(r => r.RegistrationMethod).Distinct().ToList();
            issues.Add(new HealthIssue(
                "warning",
                "multi-context-registration",
                $"{ctx.Name} is registered {ctxRegistrations.Count} times ({string.Join(", ", methods)}). Verify intentional.",
                ctxRegistrations.Select(r => $"{r.FilePath}:{r.LineNumber}").ToList()
            ));
        }

        // Category 2 — hardcoded-connection-string
        foreach (var reg in ctxRegistrations.Where(r => r.ConnectionStringSource == "Hardcoded"))
        {
            issues.Add(new HealthIssue(
                "info",
                "hardcoded-connection-string",
                $"{ctx.Name} uses a hardcoded connection string. Consider Configuration for env-specific overrides.",
                new List<string> { $"{reg.FilePath}:{reg.LineNumber}" }
            ));
        }

        // Category 3 — no-registration
        if (ctxRegistrations.Count == 0)
        {
            issues.Add(new HealthIssue(
                "warning",
                "no-registration",
                $"{ctx.Name} is defined but never registered in DI. It may be unused or configured elsewhere.",
                new List<string> { ctx.FilePath }
            ));
        }

        // Category 4 — no-migrations
        if (entities.Count > 0 && migrations.Count == 0)
        {
            issues.Add(new HealthIssue(
                "info",
                "no-migrations",
                $"{ctx.Name} has {entities.Count} entities but no migrations. Schema will not be created via EF Core migrations.",
                new List<string> { ctx.FilePath }
            ));
        }

        // Category 5 — many-manytomany
        var manyToMany = relationships.Where(r => r.RelationshipType == "ManyToMany").ToList();
        if (manyToMany.Count > 0)
        {
            var entityFilePaths = entities
                .GroupBy(e => e.EntityName)
                .ToDictionary(g => g.Key, g => g.First().EntityFilePath, StringComparer.Ordinal);

            var affected = manyToMany
                .SelectMany(r => new[] { r.SourceEntity, r.TargetEntity })
                .Distinct()
                .Select(name => entityFilePaths.TryGetValue(name, out var fp) ? fp : name)
                .ToList();

            issues.Add(new HealthIssue(
                "info",
                "many-manytomany",
                $"{ctx.Name} has {manyToMany.Count} many-to-many relationships. Ensure migrations correctly handle the join tables.",
                affected
            ));
        }

        return issues;
    }

    private static int ComputeHealthScore(List<HealthIssue> issues)
    {
        var score = 100;
        foreach (var issue in issues)
        {
            score -= issue.Severity switch
            {
                "error" => 10,
                "warning" => 5,
                "info" => 2,
                _ => 0
            };
        }
        return Math.Max(0, score);
    }

    private static string ScoreToGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F"
    };

    private static List<string> BuildRecommendations(List<HealthIssue> issues)
    {
        var recommendations = new List<string>();

        var errors = issues.Where(i => i.Severity == "error").ToList();
        if (errors.Count > 0)
        {
            recommendations.Add($"Resolve {errors.Count} error-level issue(s) first — these indicate broken or unreliable configuration.");
        }

        foreach (var group in issues.GroupBy(i => i.Category).OrderByDescending(g => g.Count()))
        {
            if (group.Key == "error") continue;

            var message = group.Key switch
            {
                "multi-context-registration" => $"Review {group.Count()} DbContext(s) registered more than once — confirm each registration is intentional.",
                "hardcoded-connection-string" => $"Consolidate {group.Count()} hardcoded connection string(s) into appsettings.json / configuration.",
                "no-registration" => $"Register {group.Count()} unregistered DbContext(s) in DI, or confirm they are configured elsewhere.",
                "no-migrations" => $"Add migrations for {group.Count()} DbContext(s) that define entities but have no migration history.",
                "many-manytomany" => $"Double-check join table handling for {group.Count()} DbContext(s) with many-to-many relationships.",
                _ => $"Review {group.Count()} issue(s) in category '{group.Key}'."
            };
            recommendations.Add(message);
        }

        return recommendations.Take(5).ToList();
    }
}
