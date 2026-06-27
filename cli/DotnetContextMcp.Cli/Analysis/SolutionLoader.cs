using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotnetContextMcp.Cli.Analysis;

public sealed class SolutionLoader : IDisposable
{
    private readonly MSBuildWorkspace _workspace;

    private SolutionLoader(MSBuildWorkspace workspace)
    {
        _workspace = workspace;
    }

    public static async Task<(SolutionLoader Loader, Solution Solution)> LoadAsync(string solutionPath)
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) =>
            Console.Error.WriteLine($"[workspace] {args.Diagnostic.Kind}: {args.Diagnostic.Message}");

        Console.Error.WriteLine($"[info] Loading solution: {solutionPath}");
        var solution = await workspace.OpenSolutionAsync(solutionPath);
        Console.Error.WriteLine($"[info] Loaded {solution.Projects.Count()} project(s)");

        return (new SolutionLoader(workspace), solution);
    }

    public void Dispose() => _workspace.Dispose();
}
