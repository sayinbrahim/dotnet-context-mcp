using EfMigrationSafety.Analyzers;
using Microsoft.CodeAnalysis.CSharp;

namespace DotnetContextMcp.Cli.Analysis;

public record SafetyIssueOutput(
    string Code,
    string AnalyzerName,
    string Severity,
    string Message,
    string? Recommendation,
    string FilePath,
    int Line
);

public record SafetySummary(int ErrorCount, int WarningCount, int InfoCount);

public static class MigrationSafetyAnalyzer
{
    // EfMigrationSafety.Analyzers does not publish stable EFMS-nnn codes itself (only
    // SafetyIssue.AnalyzerName, e.g. "NonNullableWithoutDefault"). We assign the codes
    // here until ef-migration-safety publishes them from the library directly.
    // TODO: drop this mapping once ef-migration-safety exposes its own EFMS codes.
    private static readonly IReadOnlyDictionary<string, string> CodeByAnalyzerName = new Dictionary<string, string>
    {
        ["NonNullableWithoutDefault"] = "EFMS001",
        ["DropAddColumn"] = "EFMS002",
        ["NoDataLoss"] = "EFMS003",
        ["EmptyDownMethod"] = "EFMS004",
        ["AlterColumnTruncation"] = "EFMS005",
        ["RenameOperation"] = "EFMS006",
        ["SqlInjection"] = "EFMS007",
        ["MissingIndexOnForeignKey"] = "EFMS008",
        ["PotentiallyLossyTypeConversion"] = "EFMS009",
    };

    public static readonly IReadOnlyList<IMigrationAnalyzer> AllAnalyzers = new IMigrationAnalyzer[]
    {
        new NonNullableWithoutDefaultAnalyzer(),
        new DropAddColumnAnalyzer(),
        new NoDataLossAnalyzer(),
        new EmptyDownMethodAnalyzer(),
        new AlterColumnTruncationAnalyzer(),
        new RenameOperationAnalyzer(),
        new SqlInjectionAnalyzer(),
        new MissingIndexOnForeignKeyAnalyzer(),
        new PotentiallyLossyTypeConversionAnalyzer(),
    };

    public static List<SafetyIssueOutput> Analyze(
        string sourceText, string filePath, bool includeWarnings, bool includeInfo)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
        var root = tree.GetRoot();

        var issues = new List<SafetyIssueOutput>();
        foreach (var analyzer in AllAnalyzers)
        {
            foreach (var issue in analyzer.Analyze(root, filePath))
            {
                if (issue.Severity == Severity.Warning && !includeWarnings) continue;
                if (issue.Severity == Severity.Info && !includeInfo) continue;

                issues.Add(new SafetyIssueOutput(
                    Code: CodeByAnalyzerName.GetValueOrDefault(issue.AnalyzerName, "EFMS000"),
                    AnalyzerName: issue.AnalyzerName,
                    Severity: issue.Severity.ToString().ToLowerInvariant(),
                    Message: issue.Message,
                    Recommendation: issue.Recommendation,
                    FilePath: issue.FilePath,
                    Line: issue.LineNumber
                ));
            }
        }

        return issues;
    }

    public static SafetySummary Summarize(IEnumerable<SafetyIssueOutput> issues) => new(
        ErrorCount: issues.Count(i => i.Severity == "error"),
        WarningCount: issues.Count(i => i.Severity == "warning"),
        InfoCount: issues.Count(i => i.Severity == "info")
    );
}
