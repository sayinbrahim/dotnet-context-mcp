using Microsoft.CodeAnalysis;

namespace DotnetContextMcp.Cli.Analysis;

public record MigrationInfo(
    string OwningDbContextName,
    string OwningDbContextNamespace,
    string MigrationId,
    string MigrationName,
    string ClassName,
    string Namespace,
    string FilePath,
    DateTime? Timestamp
);

public static class MigrationFinder
{
    public static async Task<(List<MigrationInfo> Migrations, int ProjectsScanned, int DbContextsWithMigrations)> FindAsync(
        Solution solution, string solutionDirectory, string? targetDbContextName = null)
    {
        var results = new List<MigrationInfo>();
        int projectsScanned = 0;
        var dbContextsWithMigrations = new HashSet<string>();

        foreach (var project in solution.Projects)
        {
            projectsScanned++;

            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                Console.Error.WriteLine($"[warn] Could not get compilation for project: {project.Name}");
                continue;
            }

            var migrationType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.Migration");
            if (migrationType is null)
                continue;

            var migrationAttributeType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute");
            var dbContextAttributeType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute");

            foreach (var type in GetAllNamedTypes(compilation.GlobalNamespace))
            {
                if (type.IsAbstract || type.TypeKind != TypeKind.Class)
                    continue;

                if (!InheritsFrom(type, migrationType))
                    continue;

                // Roslyn merges all partial declarations — GetAttributes() sees attributes from both
                // the main .cs file and the .Designer.cs file
                var attributes = type.GetAttributes();

                // Extract migration ID from [Migration("...")] attribute
                string? migrationId = null;
                if (migrationAttributeType is not null)
                {
                    var migAttr = attributes.FirstOrDefault(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, migrationAttributeType));
                    if (migAttr?.ConstructorArguments.Length > 0)
                        migrationId = migAttr.ConstructorArguments[0].Value as string;
                }

                // Fall back to parsing the source filename if attribute lookup failed
                if (migrationId is null)
                {
                    var primaryLoc = type.Locations
                        .Where(l => l.IsInSource)
                        .FirstOrDefault(l => !l.SourceTree!.FilePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
                    if (primaryLoc?.SourceTree?.FilePath is { } path)
                    {
                        var stem = Path.GetFileNameWithoutExtension(path);
                        // stem looks like "20260627141537_InitialCreate"
                        if (stem.Length > 14 && stem[14] == '_')
                            migrationId = stem;
                    }
                }

                if (migrationId is null)
                    continue;

                // Extract owning DbContext from [DbContext(typeof(...))] attribute
                string dbContextName = string.Empty;
                string dbContextNamespace = string.Empty;
                if (dbContextAttributeType is not null)
                {
                    var ctxAttr = attributes.FirstOrDefault(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, dbContextAttributeType));
                    if (ctxAttr?.ConstructorArguments.Length > 0 &&
                        ctxAttr.ConstructorArguments[0].Value is INamedTypeSymbol ctxType)
                    {
                        dbContextName = ctxType.Name;
                        dbContextNamespace = ctxType.ContainingNamespace is { IsGlobalNamespace: false } ns
                            ? ns.ToDisplayString()
                            : string.Empty;
                    }
                }

                if (targetDbContextName is not null && dbContextName != targetDbContextName)
                    continue;

                // Prefer the non-Designer source file as the canonical file path
                var primaryLocation = type.Locations
                    .Where(l => l.IsInSource)
                    .FirstOrDefault(l => !l.SourceTree!.FilePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                    ?? type.Locations.FirstOrDefault(l => l.IsInSource);

                var fullPath = primaryLocation?.SourceTree?.FilePath ?? string.Empty;
                var relativePath = GetRelativePath(solutionDirectory, fullPath);

                // Parse timestamp: first 14 chars of migration ID = yyyyMMddHHmmss
                DateTime? timestamp = null;
                if (migrationId.Length >= 14 &&
                    DateTime.TryParseExact(migrationId[..14], "yyyyMMddHHmmss",
                        null, System.Globalization.DateTimeStyles.None, out var ts))
                {
                    timestamp = ts;
                }

                // Migration name is everything after "yyyyMMddHHmmss_"
                var migrationName = migrationId.Length > 15 ? migrationId[15..] : type.Name;

                var typeNs = type.ContainingNamespace is { IsGlobalNamespace: false } typeNsSymbol
                    ? typeNsSymbol.ToDisplayString()
                    : string.Empty;

                results.Add(new MigrationInfo(
                    OwningDbContextName: dbContextName,
                    OwningDbContextNamespace: dbContextNamespace,
                    MigrationId: migrationId,
                    MigrationName: migrationName,
                    ClassName: type.Name,
                    Namespace: typeNs,
                    FilePath: relativePath,
                    Timestamp: timestamp
                ));

                if (!string.IsNullOrEmpty(dbContextName))
                    dbContextsWithMigrations.Add(dbContextName);
            }
        }

        return (results, projectsScanned, dbContextsWithMigrations.Count);
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
        try { return Path.GetRelativePath(basePath, fullPath); }
        catch { return fullPath; }
    }
}
