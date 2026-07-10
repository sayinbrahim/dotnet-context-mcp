namespace DotnetContextMcp.Cli.Plugins;

public record PluginManifest(
    string Name,
    string Version,
    string? Author,
    string? Description,
    List<CustomRule> Rules
);

public record CustomRule(
    string Id,
    string Target,
    string Check,
    string Severity,
    string Message,

    string? Pattern,
    string? Operator,
    int? Value,
    string? OperationType
);

public record PluginLoadResult(
    List<PluginManifest> LoadedPlugins,
    List<PluginLoadError> Errors,
    int TotalRulesLoaded
);

public record PluginLoadError(
    string FilePath,
    string ErrorMessage,
    string? RuleId
);
