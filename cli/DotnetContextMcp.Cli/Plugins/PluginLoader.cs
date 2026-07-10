using System.Text.Json;

namespace DotnetContextMcp.Cli.Plugins;

public class PluginLoader
{
    private readonly PluginDiscovery _discovery = new();

    public PluginLoadResult LoadPlugins(string solutionDirectory)
    {
        var files = _discovery.FindPluginFiles(solutionDirectory);
        var loaded = new List<PluginManifest>();
        var errors = new List<PluginLoadError>();

        foreach (var file in files)
        {
            var (manifest, fileErrors) = LoadPluginFile(file);
            errors.AddRange(fileErrors);
            if (manifest != null)
            {
                loaded.Add(manifest);
            }
        }

        return new PluginLoadResult(
            loaded,
            errors,
            loaded.Sum(p => p.Rules.Count)
        );
    }

    /// <summary>Loads and validates a single plugin file, returning its manifest (with only valid rules kept) alongside any errors.</summary>
    public (PluginManifest? Manifest, List<PluginLoadError> Errors) LoadPluginFile(string file)
    {
        var errors = new List<PluginLoadError>();

        try
        {
            var content = File.ReadAllText(file);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest == null)
            {
                errors.Add(new PluginLoadError(file, "Failed to parse manifest", null));
                return (null, errors);
            }

            var validRules = new List<CustomRule>();
            foreach (var rule in manifest.Rules ?? new())
            {
                var validationError = ValidateRule(rule);
                if (validationError != null)
                {
                    errors.Add(new PluginLoadError(file, validationError, rule.Id));
                }
                else
                {
                    validRules.Add(rule);
                }
            }

            // A manifest that defined rules but had every single one rejected by validation
            // contributes nothing usable — don't count the plugin itself as loaded.
            if ((manifest.Rules?.Count ?? 0) > 0 && validRules.Count == 0)
            {
                return (null, errors);
            }

            return (manifest with { Rules = validRules }, errors);
        }
        catch (JsonException ex)
        {
            errors.Add(new PluginLoadError(file, $"JSON parse error: {ex.Message}", null));
            return (null, errors);
        }
        catch (Exception ex)
        {
            errors.Add(new PluginLoadError(file, $"Unexpected error: {ex.Message}", null));
            return (null, errors);
        }
    }

    private string? ValidateRule(CustomRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id)) return "Rule Id is required";
        if (string.IsNullOrWhiteSpace(rule.Target)) return "Rule Target is required";
        if (string.IsNullOrWhiteSpace(rule.Check)) return "Rule Check is required";
        if (string.IsNullOrWhiteSpace(rule.Severity)) return "Rule Severity is required";
        if (string.IsNullOrWhiteSpace(rule.Message)) return "Rule Message is required";

        var validTargets = new[] { "dbcontext", "entity", "migration", "migration-operation" };
        if (!validTargets.Contains(rule.Target))
            return $"Invalid Target '{rule.Target}'. Must be one of: {string.Join(", ", validTargets)}";

        var validChecks = new[] { "name-regex", "entity-count", "operation-forbidden" };
        if (!validChecks.Contains(rule.Check))
            return $"Invalid Check '{rule.Check}'. Must be one of: {string.Join(", ", validChecks)}";

        var validSeverities = new[] { "info", "warning", "error" };
        if (!validSeverities.Contains(rule.Severity))
            return $"Invalid Severity '{rule.Severity}'. Must be one of: {string.Join(", ", validSeverities)}";

        switch (rule.Check)
        {
            case "name-regex":
                if (string.IsNullOrWhiteSpace(rule.Pattern))
                    return "Check 'name-regex' requires Pattern";
                try { _ = new System.Text.RegularExpressions.Regex(rule.Pattern); }
                catch (Exception ex) { return $"Invalid regex Pattern: {ex.Message}"; }
                break;

            case "entity-count":
                if (string.IsNullOrWhiteSpace(rule.Operator))
                    return "Check 'entity-count' requires Operator";
                if (rule.Value == null)
                    return "Check 'entity-count' requires Value";
                var validOps = new[] { "less-than", "less-than-or-equal", "greater-than", "greater-than-or-equal", "equal" };
                if (!validOps.Contains(rule.Operator))
                    return $"Invalid Operator '{rule.Operator}'. Must be one of: {string.Join(", ", validOps)}";
                break;

            case "operation-forbidden":
                if (string.IsNullOrWhiteSpace(rule.OperationType))
                    return "Check 'operation-forbidden' requires OperationType";
                break;
        }

        if (rule.Check == "entity-count" && rule.Target != "dbcontext")
            return "Check 'entity-count' only applies to Target 'dbcontext'";
        if (rule.Check == "operation-forbidden" && rule.Target != "migration-operation")
            return "Check 'operation-forbidden' only applies to Target 'migration-operation'";

        return null;
    }
}
