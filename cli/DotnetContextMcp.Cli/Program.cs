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

var rootCommand = new RootCommand("dotnet-context-mcp CLI - Roslyn-based .NET solution analysis");
rootCommand.AddCommand(listDbContextsCommand);

return await rootCommand.InvokeAsync(args);
