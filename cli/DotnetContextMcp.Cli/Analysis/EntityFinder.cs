using Microsoft.CodeAnalysis;

namespace DotnetContextMcp.Cli.Analysis;

public record EntityInfo(
    string DbContextName,
    string DbContextNamespace,
    string DbSetPropertyName,
    string EntityName,
    string EntityNamespace,
    string EntityFilePath
);

public static class EntityFinder
{
    public static async Task<(List<EntityInfo> Entities, int ProjectsScanned, int DbContextsFound)> FindAsync(
        Solution solution, string solutionDirectory, string? targetDbContextName = null)
    {
        var results = new List<EntityInfo>();
        int projectsScanned = 0;
        int dbContextsFound = 0;

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

            foreach (var type in GetAllNamedTypes(compilation.GlobalNamespace))
            {
                if (type.IsAbstract || type.TypeKind != TypeKind.Class)
                    continue;

                if (!InheritsFrom(type, dbContextType))
                    continue;

                if (targetDbContextName is not null && type.Name != targetDbContextName)
                    continue;

                dbContextsFound++;

                var contextName = type.Name;
                var contextNs = type.ContainingNamespace is { IsGlobalNamespace: false } ns
                    ? ns.ToDisplayString()
                    : string.Empty;

                foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
                {
                    if (member.Type is not INamedTypeSymbol propType)
                        continue;

                    if (!propType.IsGenericType || propType.Name != "DbSet")
                        continue;

                    if (propType.ContainingNamespace?.ToDisplayString() != "Microsoft.EntityFrameworkCore")
                        continue;

                    if (propType.TypeArguments.Length == 0)
                        continue;

                    var entityType = propType.TypeArguments[0];

                    var entityNs = entityType.ContainingNamespace is { IsGlobalNamespace: false } ens
                        ? ens.ToDisplayString()
                        : string.Empty;

                    string entityFilePath;
                    var location = entityType.Locations.FirstOrDefault(l => l.IsInSource);
                    if (location?.SourceTree?.FilePath is { Length: > 0 } filePath)
                        entityFilePath = GetRelativePath(solutionDirectory, filePath);
                    else
                        entityFilePath = "(external)";

                    results.Add(new EntityInfo(
                        DbContextName: contextName,
                        DbContextNamespace: contextNs,
                        DbSetPropertyName: member.Name,
                        EntityName: entityType.Name,
                        EntityNamespace: entityNs,
                        EntityFilePath: entityFilePath
                    ));
                }
            }
        }

        return (results, projectsScanned, dbContextsFound);
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
        try { return Path.GetRelativePath(basePath, fullPath); }
        catch { return fullPath; }
    }
}
