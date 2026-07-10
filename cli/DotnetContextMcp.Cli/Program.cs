using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Build.Locator;
using DotnetContextMcp.Cli.Analysis;
using DotnetContextMcp.Cli.Plugins;

MSBuildLocator.RegisterDefaults();

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var solutionArg = new Argument<string>("solution", "Path to .sln file");

var listDbContextsCommand = new Command("list-dbcontexts", "List EF Core DbContext classes in a solution");
listDbContextsCommand.AddArgument(solutionArg);
listDbContextsCommand.SetHandler(async (string solutionPath) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var (loader, solution) = await SolutionLoader.LoadAsync(solutionPath);
        using (loader)
        {
            var (dbContexts, projectsScanned, projectsWithEfCore) =
                await DbContextFinder.FindAsync(solution, solutionDirectory);

            var output = new
            {
                solution = Path.GetFileName(solutionPath),
                solutionPath,
                dbContexts,
                stats = new
                {
                    projectsScanned,
                    projectsWithEfCore,
                    dbContextsFound = dbContexts.Count
                }
            };

            Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArg);

var listEntitiesCommand = new Command("list-entities", "List EF Core entity types in a solution");
var solutionArgForEntities = new Argument<string>("solution", "Path to .sln file");
var dbContextOption = new Option<string?>("--dbcontext", "Filter to a specific DbContext by name");
listEntitiesCommand.AddArgument(solutionArgForEntities);
listEntitiesCommand.AddOption(dbContextOption);
listEntitiesCommand.SetHandler(async (string solutionPath, string? dbContextName) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var (loader, solution) = await SolutionLoader.LoadAsync(solutionPath);
        using (loader)
        {
            var (entities, projectsScanned, dbContextsFound) =
                await EntityFinder.FindAsync(solution, solutionDirectory, dbContextName);

            var grouped = entities
                .GroupBy(e => new { e.DbContextName, e.DbContextNamespace })
                .Select(g => new
                {
                    name = g.Key.DbContextName,
                    @namespace = g.Key.DbContextNamespace,
                    entities = g.Select(e => new
                    {
                        dbSetPropertyName = e.DbSetPropertyName,
                        entityName = e.EntityName,
                        entityNamespace = e.EntityNamespace,
                        entityFilePath = e.EntityFilePath
                    }).ToList()
                })
                .ToList();

            var output = new
            {
                solution = Path.GetFileName(solutionPath),
                solutionPath,
                filter = dbContextName,
                dbContexts = grouped,
                stats = new
                {
                    projectsScanned,
                    dbContextsFound,
                    entitiesFound = entities.Count
                }
            };

            Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArgForEntities, dbContextOption);

var listMigrationsCommand = new Command("list-migrations", "List EF Core migrations in a solution");
var solutionArgForMigrations = new Argument<string>("solution", "Path to .sln file");
var dbContextOptionForMigrations = new Option<string?>("--dbcontext", "Filter to a specific DbContext by name");
listMigrationsCommand.AddArgument(solutionArgForMigrations);
listMigrationsCommand.AddOption(dbContextOptionForMigrations);
listMigrationsCommand.SetHandler(async (string solutionPath, string? dbContextName) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var (loader, solution) = await SolutionLoader.LoadAsync(solutionPath);
        using (loader)
        {
            var (migrations, projectsScanned, dbContextsWithMigrations) =
                await MigrationFinder.FindAsync(solution, solutionDirectory, dbContextName);

            var grouped = migrations
                .GroupBy(m => new { m.OwningDbContextName, m.OwningDbContextNamespace })
                .Select(g => new
                {
                    name = g.Key.OwningDbContextName,
                    @namespace = g.Key.OwningDbContextNamespace,
                    migrations = g
                        .OrderBy(m => m.Timestamp ?? DateTime.MaxValue)
                        .ThenBy(m => m.MigrationId)
                        .Select(m => new
                        {
                            id = m.MigrationId,
                            name = m.MigrationName,
                            className = m.ClassName,
                            @namespace = m.Namespace,
                            filePath = m.FilePath,
                            timestamp = m.Timestamp
                        }).ToList(),
                    migrationCount = g.Count()
                })
                .ToList();

            var allTimestamps = migrations
                .Where(m => m.Timestamp.HasValue)
                .Select(m => m.Timestamp!.Value)
                .ToList();

            var output = new
            {
                solution = Path.GetFileName(solutionPath),
                solutionPath,
                filter = dbContextName,
                dbContexts = grouped,
                stats = new
                {
                    projectsScanned,
                    dbContextsWithMigrations,
                    totalMigrations = migrations.Count,
                    earliestMigration = allTimestamps.Count > 0 ? (DateTime?)allTimestamps.Min() : null,
                    latestMigration = allTimestamps.Count > 0 ? (DateTime?)allTimestamps.Max() : null
                }
            };

            Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArgForMigrations, dbContextOptionForMigrations);

var analyzeMigrationCommand = new Command("analyze-migration", "Analyze Up/Down operations in a specific EF Core migration");
var solutionArgForAnalyze = new Argument<string>("solution", "Path to .sln file");
var migrationIdArg = new Argument<string>("migrationId", "Migration ID (e.g. 20260627141537_InitialCreate)");
analyzeMigrationCommand.AddArgument(solutionArgForAnalyze);
analyzeMigrationCommand.AddArgument(migrationIdArg);
analyzeMigrationCommand.SetHandler(async (string solutionPath, string migrationId) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var (loader, solution) = await SolutionLoader.LoadAsync(solutionPath);
        using (loader)
        {
            var analyzed = await MigrationOperationAnalyzer.AnalyzeAsync(solution, solutionDirectory, migrationId);

            if (analyzed is null)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    error = "Migration not found",
                    migrationId
                }, jsonOptions));
                Environment.Exit(1);
                return;
            }

            var output = new
            {
                migration = new
                {
                    id = analyzed.MigrationId,
                    name = analyzed.MigrationName,
                    dbContext = analyzed.OwningDbContext,
                    filePath = analyzed.FilePath
                },
                upOperations = analyzed.UpOperations,
                downOperations = analyzed.DownOperations,
                summary = analyzed.Summary
            };

            Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArgForAnalyze, migrationIdArg);


var listRelationshipsCommand = new Command("list-relationships", "List EF Core entity relationships (navigation properties, foreign keys) in a solution");
var solutionArgForRelationships = new Argument<string>("solution", "Path to .sln file");
var dbContextArgForRelationships = new Argument<string>("dbcontext", "DbContext name to analyze relationships for");
var entityOptionForRelationships = new Option<string?>("--entity", "Filter to a specific entity by name");
listRelationshipsCommand.AddArgument(solutionArgForRelationships);
listRelationshipsCommand.AddArgument(dbContextArgForRelationships);
listRelationshipsCommand.AddOption(entityOptionForRelationships);
listRelationshipsCommand.SetHandler(async (string solutionPath, string dbContextName, string? entityName) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var (loader, solution) = await SolutionLoader.LoadAsync(solutionPath);
        using (loader)
        {
            var (dbContexts, _, _) = await DbContextFinder.FindAsync(solution, solutionDirectory);
            if (!dbContexts.Any(d => d.Name == dbContextName))
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    error = "DbContext not found",
                    details = $"No DbContext named '{dbContextName}' was found in the solution. Available: {string.Join(", ", dbContexts.Select(d => d.Name))}"
                }, jsonOptions));
                Environment.Exit(1);
                return;
            }

            var finder = new RelationshipFinder();
            var relationships = finder.Find(solution, dbContextName, entityName);

            var byType = new Dictionary<string, int>
            {
                ["OneToMany"] = relationships.Count(r => r.RelationshipType == "OneToMany"),
                ["ManyToOne"] = relationships.Count(r => r.RelationshipType == "ManyToOne"),
                ["OneToOne"] = relationships.Count(r => r.RelationshipType == "OneToOne"),
                ["ManyToMany"] = relationships.Count(r => r.RelationshipType == "ManyToMany")
            };

            var output = new
            {
                solution = Path.GetFileName(solutionPath),
                dbContextName,
                filter = new
                {
                    entity = entityName
                },
                relationships,
                stats = new
                {
                    totalRelationships = relationships.Count,
                    entitiesAnalyzed = finder.EntitiesAnalyzed,
                    byType
                }
            };

            Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArgForRelationships, dbContextArgForRelationships, entityOptionForRelationships);

var findDbContextDependenciesCommand = new Command("find-dbcontext-dependencies", "Find DI registrations of EF Core DbContexts (AddDbContext, AddDbContextPool, AddDbContextFactory) in a solution");
var solutionArgForDbContextDependencies = new Argument<string>("solution", "Path to .sln file");
findDbContextDependenciesCommand.AddArgument(solutionArgForDbContextDependencies);
findDbContextDependenciesCommand.SetHandler(async (string solutionPath) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var (loader, solution) = await SolutionLoader.LoadAsync(solutionPath);
        using (loader)
        {
            var finder = new DbContextRegistrationFinder();
            var (registrations, stats) = finder.Find(solution);

            var output = new
            {
                solution = Path.GetFileName(solutionPath),
                registrations,
                stats = new
                {
                    totalRegistrations = stats.TotalRegistrations,
                    byMethod = stats.ByMethod,
                    byProvider = stats.ByProvider
                }
            };

            Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArgForDbContextDependencies);

var analyzeSolutionHealthCommand = new Command("analyze-solution-health", "Aggregate DbContext/entity/migration/relationship/registration data into a solution health report");
var solutionArgForHealth = new Argument<string>("solution", "Path to .sln file");
var includeCustomOption = new Option<bool>("--include-custom", "Include custom analyzer plugin results in the health report");
analyzeSolutionHealthCommand.AddArgument(solutionArgForHealth);
analyzeSolutionHealthCommand.AddOption(includeCustomOption);
analyzeSolutionHealthCommand.SetHandler(async (string solutionPath, bool includeCustom) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var (loader, solution) = await SolutionLoader.LoadAsync(solutionPath);
        using (loader)
        {
            var analyzer = new SolutionHealthAnalyzer();
            var report = await analyzer.AnalyzeAsync(solution, solutionPath, includeCustom);

            var output = new
            {
                solutionPath = Path.GetFileName(solutionPath),
                summary = report.Summary,
                issues = report.Issues,
                recommendations = report.Recommendations,
                byDbContext = report.ByDbContext
            };

            Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArgForHealth, includeCustomOption);

var pluginListCommand = new Command("plugin-list", "Discover and load custom analyzer plugin JSON files for a solution");
var solutionArgForPluginList = new Argument<string>("solution", "Path to .sln file");
pluginListCommand.AddArgument(solutionArgForPluginList);
pluginListCommand.SetHandler((string solutionPath) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var discovery = new PluginDiscovery();
        var loader = new PluginLoader();
        var files = discovery.FindPluginFiles(solutionDirectory);

        var plugins = new List<object>();
        var allErrors = new List<PluginLoadError>();
        var totalRulesLoaded = 0;

        foreach (var file in files)
        {
            var (manifest, fileErrors) = loader.LoadPluginFile(file);
            allErrors.AddRange(fileErrors);

            if (manifest != null)
            {
                totalRulesLoaded += manifest.Rules.Count;
                plugins.Add(new
                {
                    name = manifest.Name,
                    version = manifest.Version,
                    filePath = Path.GetRelativePath(solutionDirectory, file).Replace('\\', '/'),
                    ruleCount = manifest.Rules.Count,
                    rules = manifest.Rules.Select(r => new
                    {
                        id = r.Id,
                        target = r.Target,
                        check = r.Check,
                        severity = r.Severity
                    }).ToList()
                });
            }
        }

        var errors = allErrors.Select(e => new
        {
            filePath = Path.GetRelativePath(solutionDirectory, e.FilePath).Replace('\\', '/'),
            ruleId = e.RuleId,
            errorMessage = e.ErrorMessage
        }).ToList();

        var output = new
        {
            solutionPath = Path.GetFileName(solutionPath),
            totalPluginsLoaded = plugins.Count,
            totalRulesLoaded,
            plugins,
            errors
        };

        Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArgForPluginList);

var runCustomAnalyzersCommand = new Command("run-custom-analyzers", "Discover, load, and execute custom analyzer plugin rules against a solution");
var solutionArgForCustomAnalyzers = new Argument<string>("solution", "Path to .sln file");
runCustomAnalyzersCommand.AddArgument(solutionArgForCustomAnalyzers);
runCustomAnalyzersCommand.SetHandler(async (string solutionPath) =>
{
    solutionPath = Path.GetFullPath(solutionPath);

    if (!File.Exists(solutionPath))
    {
        Console.Error.WriteLine($"[error] Solution file not found: {solutionPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = "Solution file not found",
            details = solutionPath
        }, jsonOptions));
        Environment.Exit(1);
        return;
    }

    try
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var pluginLoader = new PluginLoader();
        var loadResult = pluginLoader.LoadPlugins(solutionDirectory);

        var (loader, solution) = await SolutionLoader.LoadAsync(solutionPath);
        using (loader)
        {
            var ruleEngine = new RuleEngine();
            var execResult = await ruleEngine.ExecuteAsync(solution, solutionDirectory, loadResult.LoadedPlugins);

            var output = new
            {
                solutionPath = Path.GetFileName(solutionPath),
                pluginsLoaded = loadResult.LoadedPlugins.Count,
                rulesEvaluated = execResult.RulesEvaluated,
                issuesFound = execResult.IssuesFound,
                issues = execResult.Issues.Select(i => new
                {
                    ruleId = i.RuleId,
                    pluginName = i.PluginName,
                    severity = i.Severity,
                    category = i.Category,
                    message = i.Message,
                    affectedItems = i.AffectedItems
                }).ToList(),
                warnings = execResult.Warnings
            };

            Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {ex}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            error = ex.Message,
            details = ex.ToString()
        }, jsonOptions));
        Environment.Exit(1);
    }
}, solutionArgForCustomAnalyzers);

var rootCommand = new RootCommand("dotnet-context-mcp CLI - Roslyn-based .NET solution analysis");
rootCommand.AddCommand(listDbContextsCommand);
rootCommand.AddCommand(listEntitiesCommand);
rootCommand.AddCommand(listMigrationsCommand);
rootCommand.AddCommand(analyzeMigrationCommand);
rootCommand.AddCommand(listRelationshipsCommand);
rootCommand.AddCommand(findDbContextDependenciesCommand);
rootCommand.AddCommand(analyzeSolutionHealthCommand);
rootCommand.AddCommand(pluginListCommand);
rootCommand.AddCommand(runCustomAnalyzersCommand);

return await rootCommand.InvokeAsync(args);
