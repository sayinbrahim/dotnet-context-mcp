using Microsoft.CodeAnalysis;
using DotnetContextMcp.Cli.Analysis;

namespace DotnetContextMcp.Cli.Plugins;

public record CustomRuleIssue(
    string RuleId,
    string PluginName,
    string Severity,
    string Category,
    string Message,
    List<string> AffectedItems
);

public record RuleExecutionResult(
    int RulesEvaluated,
    int IssuesFound,
    List<CustomRuleIssue> Issues,
    List<string> Warnings
);

public class RuleEngine
{
    public async Task<RuleExecutionResult> ExecuteAsync(
        Solution solution,
        string solutionDirectory,
        List<PluginManifest> plugins)
    {
        var issues = new List<CustomRuleIssue>();
        var warnings = new List<string>();
        int rulesEvaluated = 0;

        var (dbContexts, _, _) = await DbContextFinder.FindAsync(solution, solutionDirectory);
        var uniqueContexts = dbContexts
            .GroupBy(x => $"{x.Namespace}.{x.Name}")
            .Select(g => g.First())
            .ToList();

        foreach (var plugin in plugins)
        {
            foreach (var rule in plugin.Rules)
            {
                rulesEvaluated++;

                try
                {
                    switch (rule.Check)
                    {
                        case "name-regex":
                            await ExecuteNameRegexRule(rule, plugin.Name, uniqueContexts, solution, solutionDirectory, issues);
                            break;

                        case "entity-count":
                            await ExecuteEntityCountRule(rule, plugin.Name, uniqueContexts, solution, solutionDirectory, issues);
                            break;

                        case "operation-forbidden":
                            await ExecuteOperationForbiddenRule(rule, plugin.Name, uniqueContexts, solution, solutionDirectory, issues);
                            break;

                        default:
                            warnings.Add($"Rule {rule.Id}: unknown check '{rule.Check}' — skipped");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Rule {rule.Id}: execution failed — {ex.Message}");
                }
            }
        }

        return new RuleExecutionResult(
            rulesEvaluated,
            issues.Count,
            issues,
            warnings
        );
    }

    private async Task ExecuteNameRegexRule(
        CustomRule rule,
        string pluginName,
        List<DbContextInfo> dbContexts,
        Solution solution,
        string solutionDirectory,
        List<CustomRuleIssue> issues)
    {
        var regex = new System.Text.RegularExpressions.Regex(rule.Pattern!);

        if (rule.Target == "dbcontext")
        {
            foreach (var ctx in dbContexts)
            {
                if (!regex.IsMatch(ctx.Name))
                {
                    issues.Add(new CustomRuleIssue(
                        rule.Id,
                        pluginName,
                        rule.Severity,
                        "custom-rule",
                        RenderMessage(rule.Message, new Dictionary<string, string>
                        {
                            ["name"] = ctx.Name,
                            ["namespace"] = ctx.Namespace
                        }),
                        new List<string> { string.IsNullOrEmpty(ctx.FilePath) ? ctx.Name : ctx.FilePath }
                    ));
                }
            }
        }
        else if (rule.Target == "entity")
        {
            foreach (var ctx in dbContexts)
            {
                var (entities, _, _) = await EntityFinder.FindAsync(solution, solutionDirectory, ctx.Name);

                foreach (var entity in entities)
                {
                    if (!regex.IsMatch(entity.EntityName))
                    {
                        issues.Add(new CustomRuleIssue(
                            rule.Id,
                            pluginName,
                            rule.Severity,
                            "custom-rule",
                            RenderMessage(rule.Message, new Dictionary<string, string>
                            {
                                ["name"] = entity.EntityName,
                                ["dbContext"] = ctx.Name
                            }),
                            new List<string> { string.IsNullOrEmpty(entity.EntityFilePath) ? entity.EntityName : entity.EntityFilePath }
                        ));
                    }
                }
            }
        }
    }

    private async Task ExecuteEntityCountRule(
        CustomRule rule,
        string pluginName,
        List<DbContextInfo> dbContexts,
        Solution solution,
        string solutionDirectory,
        List<CustomRuleIssue> issues)
    {
        if (rule.Target != "dbcontext") return; // Validated in loader but defensive

        foreach (var ctx in dbContexts)
        {
            var (entities, _, _) = await EntityFinder.FindAsync(solution, solutionDirectory, ctx.Name);
            int count = entities.Count;

            bool failsCheck = rule.Operator switch
            {
                "less-than" => !(count < rule.Value),
                "less-than-or-equal" => !(count <= rule.Value),
                "greater-than" => !(count > rule.Value),
                "greater-than-or-equal" => !(count >= rule.Value),
                "equal" => count != rule.Value,
                _ => false
            };

            if (failsCheck)
            {
                issues.Add(new CustomRuleIssue(
                    rule.Id,
                    pluginName,
                    rule.Severity,
                    "custom-rule",
                    RenderMessage(rule.Message, new Dictionary<string, string>
                    {
                        ["name"] = ctx.Name,
                        ["count"] = count.ToString(),
                        ["value"] = rule.Value?.ToString() ?? "0"
                    }),
                    new List<string> { string.IsNullOrEmpty(ctx.FilePath) ? ctx.Name : ctx.FilePath }
                ));
            }
        }
    }

    private async Task ExecuteOperationForbiddenRule(
        CustomRule rule,
        string pluginName,
        List<DbContextInfo> dbContexts,
        Solution solution,
        string solutionDirectory,
        List<CustomRuleIssue> issues)
    {
        if (rule.Target != "migration-operation") return;

        var forbiddenOp = rule.OperationType!;

        foreach (var ctx in dbContexts)
        {
            var (migrations, _, _) = await MigrationFinder.FindAsync(solution, solutionDirectory, ctx.Name);

            foreach (var mig in migrations)
            {
                var analyzed = await MigrationOperationAnalyzer.AnalyzeAsync(solution, solutionDirectory, mig.MigrationId);

                if (analyzed == null) continue;

                foreach (var op in analyzed.UpOperations)
                {
                    if (op.Type == forbiddenOp)
                    {
                        issues.Add(new CustomRuleIssue(
                            rule.Id,
                            pluginName,
                            rule.Severity,
                            "custom-rule",
                            RenderMessage(rule.Message, new Dictionary<string, string>
                            {
                                ["migrationName"] = mig.MigrationName,
                                ["migrationId"] = mig.MigrationId,
                                ["operationType"] = op.Type,
                                ["tableName"] = op.TableName ?? ""
                            }),
                            new List<string> { analyzed.FilePath }
                        ));
                    }
                }
            }
        }
    }

    private string RenderMessage(string template, Dictionary<string, string> placeholders)
    {
        string result = template;
        foreach (var kvp in placeholders)
        {
            result = result.Replace($"{{{kvp.Key}}}", kvp.Value);
        }
        return result;
    }
}
