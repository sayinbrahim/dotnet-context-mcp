using System.CommandLine;
using System.Text.Json;
using Microsoft.Build.Locator;
using DotnetContextMcp.Cli.Analysis;

MSBuildLocator.RegisterDefaults();

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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

var rootCommand = new RootCommand("dotnet-context-mcp CLI - Roslyn-based .NET solution analysis");
rootCommand.AddCommand(listDbContextsCommand);
rootCommand.AddCommand(listEntitiesCommand);
rootCommand.AddCommand(listMigrationsCommand);

return await rootCommand.InvokeAsync(args);
