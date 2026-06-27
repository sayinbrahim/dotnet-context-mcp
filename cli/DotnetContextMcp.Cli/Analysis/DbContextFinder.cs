using Microsoft.CodeAnalysis;

namespace DotnetContextMcp.Cli.Analysis;

public record DbContextInfo(
    string Name,
    string Namespace,
    string ProjectName,
    string FilePath
);

public static class DbContextFinder
{
    public static async Task<(List<DbContextInfo> DbContexts, int ProjectsScanned, int ProjectsWithEfCore)> FindAsync(
        Solution solution, string solutionDirectory)
    {
        var results = new List<DbContextInfo>();
        int projectsScanned = 0;
        int projectsWithEfCore = 0;

        foreach (var project in solution.Projects)
        {
            projectsScanned++;

            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                Console.Error.WriteLine($"[warn] Could not get compilation for project: {project.Name}");
                continue;
            }

            var dbContextType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            if (dbContextType is null)
                continue;

            projectsWithEfCore++;

            foreach (var type in GetAllNamedTypes(compilation.GlobalNamespace))
            {
                if (type.IsAbstract || type.TypeKind != TypeKind.Class)
                    continue;

                if (!InheritsFrom(type, dbContextType))
                    continue;

                var location = type.Locations.FirstOrDefault(l => l.IsInSource);
                var fullPath = location?.SourceTree?.FilePath ?? string.Empty;
                var relativePath = GetRelativePath(solutionDirectory, fullPath);
                var ns = type.ContainingNamespace;

                results.Add(new DbContextInfo(
                    Name: type.Name,
                    Namespace: ns is null || ns.IsGlobalNamespace ? string.Empty : ns.ToDisplayString(),
                    ProjectName: project.Name,
                    FilePath: relativePath
                ));
            }
        }

        return (results, projectsScanned, projectsWithEfCore);
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol targetBase)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, targetBase.OriginalDefinition))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in GetNestedTypes(type))
                yield return nested;
        }
        foreach (var childNs in ns.GetNamespaceMembers())
            foreach (var type in GetAllNamedTypes(childNs))
                yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var t in GetNestedTypes(nested))
                yield return t;
        }
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return string.Empty;
        try
        {
            return Path.GetRelativePath(basePath, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }
}
