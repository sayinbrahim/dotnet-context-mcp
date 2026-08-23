using Microsoft.CodeAnalysis;

namespace DotnetContextMcp.Cli.Analysis;

public record IssueCodeCount(string Code, int Count);

public record DbContextSafety(
    string DbContextName,
    int MigrationCount,
    int IssueCount,
    List<IssueCodeCount> TopIssues
);

public record SolutionSafetyReport(
    string SolutionPath,
    int TotalMigrations,
    int TotalIssues,
    List<DbContextSafety> ByDbContext,
    int SafetyScore,
    string Grade
);

public static class SolutionSafetyReportAnalyzer
{
    public static async Task<SolutionSafetyReport> AnalyzeAsync(Solution solution, string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        var (migrations, _, _) = await MigrationFinder.FindAsync(solution, solutionDirectory);

        var byDbContextGroups = migrations
            .GroupBy(m => string.IsNullOrEmpty(m.OwningDbContextName) ? "(unknown)" : m.OwningDbContextName);

        var byDbContext = new List<DbContextSafety>();
        var allIssues = new List<SafetyIssueOutput>();

        foreach (var group in byDbContextGroups)
        {
            var ctxIssues = new List<SafetyIssueOutput>();

            foreach (var migration in group)
            {
                var fullPath = Path.IsPathRooted(migration.FilePath)
                    ? migration.FilePath
                    : Path.GetFullPath(Path.Combine(solutionDirectory, migration.FilePath));

                if (!File.Exists(fullPath))
                {
                    Console.Error.WriteLine($"[warn] Migration file not found on disk, skipping: {fullPath}");
                    continue;
                }

                var sourceText = await File.ReadAllTextAsync(fullPath);
                // Aggregate scoring needs every severity, regardless of the single-file command's
                // default include-warnings/include-info flags.
                var issues = MigrationSafetyAnalyzer.Analyze(sourceText, fullPath, includeWarnings: true, includeInfo: true);
                ctxIssues.AddRange(issues);
            }

            allIssues.AddRange(ctxIssues);

            var topIssues = ctxIssues
                .GroupBy(i => i.Code)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => new IssueCodeCount(g.Key, g.Count()))
                .ToList();

            byDbContext.Add(new DbContextSafety(
                DbContextName: group.Key,
                MigrationCount: group.Count(),
                IssueCount: ctxIssues.Count,
                TopIssues: topIssues
            ));
        }

        var safetyScore = ComputeSafetyScore(allIssues);

        return new SolutionSafetyReport(
            SolutionPath: solutionPath,
            TotalMigrations: migrations.Count,
            TotalIssues: allIssues.Count,
            ByDbContext: byDbContext,
            SafetyScore: safetyScore,
            Grade: ScoreToGrade(safetyScore)
        );
    }

    private static int ComputeSafetyScore(List<SafetyIssueOutput> issues)
    {
        double score = 100;
        foreach (var issue in issues)
        {
            score -= issue.Severity switch
            {
                "error" => 5,
                "warning" => 2,
                "info" => 0.5,
                _ => 0
            };
        }
        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }

    private static string ScoreToGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F"
    };
}
