using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetContextMcp.Cli.Analysis;

public record ColumnInfo(
    string Name,
    string Type,
    bool Nullable,
    bool IsPrimaryKey,
    string? DefaultValue
);

public record MigrationOperation
{
    public required string Type { get; init; }
    public string? TableName { get; init; }
    public string? ColumnName { get; init; }
    public string? ColumnType { get; init; }
    public bool? Nullable { get; init; }
    public string? DefaultValue { get; init; }
    public List<ColumnInfo>? Columns { get; init; }

    // Indexes / primary keys
    public string? IndexName { get; init; }
    public List<string>? IndexColumns { get; init; }
    public bool? IsUnique { get; init; }

    // Renames
    public string? NewName { get; init; }
    public string? Schema { get; init; }
    public string? NewSchema { get; init; }

    // Foreign keys
    public string? ForeignKeyName { get; init; }
    public string? PrincipalTable { get; init; }
    public string? PrincipalColumn { get; init; }
    public string? OnDelete { get; init; }

    // Raw SQL
    public string? SqlPreview { get; init; }

    // Unknown operations only
    public Dictionary<string, string>? RawArgs { get; init; }
}

public record MigrationSummary(
    int TotalUpOperations,
    int TotalDownOperations,
    int TablesCreated,
    int TablesDropped,
    int ColumnsAdded,
    int ColumnsDropped,
    int ColumnsAltered,
    int IndexesCreated,
    int IndexesDropped,
    int RenamesPerformed,
    int ForeignKeysAdded,
    int ForeignKeysDropped,
    int RawSqlExecutions,
    int UnknownOperations
);

public record AnalyzedMigration(
    string MigrationId,
    string MigrationName,
    string OwningDbContext,
    string FilePath,
    List<MigrationOperation> UpOperations,
    List<MigrationOperation> DownOperations,
    MigrationSummary Summary
);

public static class MigrationOperationAnalyzer
{
    public static async Task<AnalyzedMigration?> AnalyzeAsync(
        Solution solution, string solutionDirectory, string migrationId)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null) continue;

            var migrationType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.Migration");
            if (migrationType is null) continue;

            var migrationAttributeType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute");
            var dbContextAttributeType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute");

            foreach (var type in GetAllNamedTypes(compilation.GlobalNamespace))
            {
                if (type.IsAbstract || type.TypeKind != TypeKind.Class) continue;
                if (!InheritsFrom(type, migrationType)) continue;

                string? foundMigrationId = null;
                if (migrationAttributeType is not null)
                {
                    var migAttr = type.GetAttributes().FirstOrDefault(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, migrationAttributeType));
                    if (migAttr?.ConstructorArguments.Length > 0)
                        foundMigrationId = migAttr.ConstructorArguments[0].Value as string;
                }

                if (foundMigrationId != migrationId) continue;

                string dbContextName = string.Empty;
                if (dbContextAttributeType is not null)
                {
                    var ctxAttr = type.GetAttributes().FirstOrDefault(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, dbContextAttributeType));
                    if (ctxAttr?.ConstructorArguments.Length > 0 &&
                        ctxAttr.ConstructorArguments[0].Value is INamedTypeSymbol ctxType)
                        dbContextName = ctxType.Name;
                }

                var primaryLocation = type.Locations
                    .Where(l => l.IsInSource)
                    .FirstOrDefault(l => !l.SourceTree!.FilePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                    ?? type.Locations.FirstOrDefault(l => l.IsInSource);

                if (primaryLocation?.SourceTree is null) continue;

                var syntaxRoot = await primaryLocation.SourceTree.GetRootAsync();
                var methods = syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

                var upMethod = methods.FirstOrDefault(m => m.Identifier.Text == "Up");
                var downMethod = methods.FirstOrDefault(m => m.Identifier.Text == "Down");

                if (upMethod is null)
                    Console.Error.WriteLine($"[warn] Up() not found in migration {migrationId}");
                if (downMethod is null)
                    Console.Error.WriteLine($"[warn] Down() not found in migration {migrationId}");

                var upOps = upMethod is not null ? ParseMethodOperations(upMethod) : new List<MigrationOperation>();
                var downOps = downMethod is not null ? ParseMethodOperations(downMethod) : new List<MigrationOperation>();
                var allOps = upOps.Concat(downOps).ToList();

                var migrationName = migrationId.Length > 15 ? migrationId[15..] : type.Name;
                var relativePath = GetRelativePath(solutionDirectory, primaryLocation.SourceTree.FilePath);

                var summary = new MigrationSummary(
                    TotalUpOperations: upOps.Count,
                    TotalDownOperations: downOps.Count,
                    TablesCreated: upOps.Count(o => o.Type == "CreateTable"),
                    TablesDropped: downOps.Count(o => o.Type == "DropTable"),
                    ColumnsAdded: upOps.Count(o => o.Type == "AddColumn"),
                    ColumnsDropped: downOps.Count(o => o.Type == "DropColumn"),
                    ColumnsAltered: allOps.Count(o => o.Type == "AlterColumn"),
                    IndexesCreated: upOps.Count(o => o.Type == "CreateIndex"),
                    IndexesDropped: downOps.Count(o => o.Type == "DropIndex"),
                    RenamesPerformed: allOps.Count(o => o.Type is "RenameColumn" or "RenameTable"),
                    ForeignKeysAdded: upOps.Count(o => o.Type == "AddForeignKey"),
                    ForeignKeysDropped: downOps.Count(o => o.Type == "DropForeignKey"),
                    RawSqlExecutions: allOps.Count(o => o.Type == "Sql"),
                    UnknownOperations: allOps.Count(o => o.Type == "Unknown")
                );

                return new AnalyzedMigration(
                    MigrationId: migrationId,
                    MigrationName: migrationName,
                    OwningDbContext: dbContextName,
                    FilePath: relativePath,
                    UpOperations: upOps,
                    DownOperations: downOps,
                    Summary: summary
                );
            }
        }

        return null;
    }

    private static List<MigrationOperation> ParseMethodOperations(MethodDeclarationSyntax method)
    {
        var operations = new List<MigrationOperation>();
        if (method.Body is null) return operations;

        foreach (var statement in method.Body.Statements)
        {
            if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
                continue;

            var op = ParseOperation(invocation);
            if (op is not null)
                operations.Add(op);
        }

        return operations;
    }

    private static MigrationOperation? ParseOperation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var receiverName = memberAccess.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
        if (receiverName != "migrationBuilder") return null;

        var operationName = memberAccess.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax gen => gen.Identifier.Text,
            _ => null
        };
        if (operationName is null) return null;

        var rawArgs = ExtractNamedArgs(invocation.ArgumentList);

        return operationName switch
        {
            "CreateTable" => ParseCreateTable(rawArgs, invocation),
            "DropTable" => ParseDropTable(rawArgs),
            "AddColumn" => ParseAddColumn(rawArgs, memberAccess),
            "DropColumn" => ParseDropColumn(rawArgs),
            "AlterColumn" => ParseAlterColumn(rawArgs, memberAccess),
            "CreateIndex" => ParseCreateIndex(rawArgs, invocation),
            "DropIndex" => ParseDropIndex(rawArgs),
            "RenameColumn" => ParseRenameColumn(rawArgs),
            "RenameTable" => ParseRenameTable(rawArgs),
            "AddForeignKey" => ParseAddForeignKey(rawArgs, invocation),
            "DropForeignKey" => ParseDropForeignKey(rawArgs),
            "AddPrimaryKey" => ParseAddPrimaryKey(rawArgs, invocation),
            "DropPrimaryKey" => ParseDropPrimaryKey(rawArgs),
            "Sql" => ParseSql(invocation),
            _ => new MigrationOperation
            {
                Type = "Unknown",
                TableName = rawArgs.GetValueOrDefault("name") ?? rawArgs.GetValueOrDefault("table"),
                RawArgs = new Dictionary<string, string>(rawArgs) { ["operationName"] = operationName }
            }
        };
    }

    // ── CreateTable ─────────────────────────────────────────────────────────

    private static MigrationOperation ParseCreateTable(
        Dictionary<string, string> rawArgs, InvocationExpressionSyntax invocation)
    {
        var columnsArg = invocation.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "columns");
        var constraintsArg = invocation.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "constraints");

        var primaryKeys = ParsePrimaryKeys(constraintsArg);
        var columns = ParseColumnLambda(columnsArg, primaryKeys);

        return new MigrationOperation
        {
            Type = "CreateTable",
            TableName = rawArgs.GetValueOrDefault("name"),
            Columns = columns
        };
    }

    private static HashSet<string> ParsePrimaryKeys(ArgumentSyntax? constraintsArg)
    {
        var pks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (constraintsArg is null) return pks;

        var pkCalls = constraintsArg.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv =>
                inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.Text == "PrimaryKey");

        foreach (var pkCall in pkCalls)
        {
            if (pkCall.ArgumentList.Arguments.Count < 2) continue;
            if (pkCall.ArgumentList.Arguments[1].Expression is SimpleLambdaExpressionSyntax lambda)
                ExtractMemberNames(lambda.Body, pks);
        }

        return pks;
    }

    private static void ExtractMemberNames(CSharpSyntaxNode body, HashSet<string> names)
    {
        if (body is MemberAccessExpressionSyntax memberAccess)
            names.Add(memberAccess.Name.Identifier.Text);
        else if (body is AnonymousObjectCreationExpressionSyntax anonObj)
            foreach (var init in anonObj.Initializers)
                if (init.Expression is MemberAccessExpressionSyntax ma)
                    names.Add(ma.Name.Identifier.Text);
    }

    private static List<ColumnInfo> ParseColumnLambda(ArgumentSyntax? columnsArg, HashSet<string> primaryKeys)
    {
        var columns = new List<ColumnInfo>();
        if (columnsArg is null) return columns;

        var lambda = columnsArg.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>().FirstOrDefault();
        if (lambda?.Body is not AnonymousObjectCreationExpressionSyntax anonObj) return columns;

        foreach (var initializer in anonObj.Initializers)
        {
            var colName = initializer.NameEquals?.Name.Identifier.Text;
            if (colName is null) continue;

            var colInv = FindColumnInvocation(initializer.Expression);
            if (colInv is null) continue;

            var colArgs = ExtractNamedArgs(colInv.ArgumentList);
            var clrType = ExtractGenericTypeArg(colInv);
            var sqlType = colArgs.GetValueOrDefault("type") ?? clrType ?? string.Empty;

            var nullableStr = colArgs.GetValueOrDefault("nullable");
            bool nullable = nullableStr?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            var defaultValue = colArgs.TryGetValue("defaultValue", out var dv) ? dv : null;

            columns.Add(new ColumnInfo(
                Name: colName,
                Type: sqlType,
                Nullable: nullable,
                IsPrimaryKey: primaryKeys.Contains(colName),
                DefaultValue: defaultValue
            ));
        }

        return columns;
    }

    private static InvocationExpressionSyntax? FindColumnInvocation(ExpressionSyntax expr)
    {
        if (expr is not InvocationExpressionSyntax inv) return null;
        if (inv.Expression is not MemberAccessExpressionSyntax ma) return null;

        if (ma.Name.Identifier.Text == "Column")
            return inv;

        // Method chain: table.Column<T>(...).Annotation(...) — drill through
        return FindColumnInvocation(ma.Expression);
    }

    private static string? ExtractGenericTypeArg(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax gen })
            return gen.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
        return null;
    }

    // ── DropTable ────────────────────────────────────────────────────────────

    private static MigrationOperation ParseDropTable(Dictionary<string, string> rawArgs) =>
        new() { Type = "DropTable", TableName = rawArgs.GetValueOrDefault("name") };

    // ── AddColumn / DropColumn ───────────────────────────────────────────────

    private static MigrationOperation ParseAddColumn(
        Dictionary<string, string> rawArgs, MemberAccessExpressionSyntax memberAccess)
    {
        var sqlType = rawArgs.GetValueOrDefault("type");
        if (sqlType is null && memberAccess.Name is GenericNameSyntax gen)
            sqlType = gen.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();

        var nullableStr = rawArgs.GetValueOrDefault("nullable");
        bool? nullable = nullableStr is null ? null : nullableStr.Equals("true", StringComparison.OrdinalIgnoreCase);

        return new MigrationOperation
        {
            Type = "AddColumn",
            TableName = rawArgs.GetValueOrDefault("table"),
            ColumnName = rawArgs.GetValueOrDefault("name"),
            ColumnType = sqlType,
            Nullable = nullable,
            DefaultValue = rawArgs.TryGetValue("defaultValue", out var dv) ? dv : null
        };
    }

    private static MigrationOperation ParseDropColumn(Dictionary<string, string> rawArgs) =>
        new()
        {
            Type = "DropColumn",
            TableName = rawArgs.GetValueOrDefault("table"),
            ColumnName = rawArgs.GetValueOrDefault("name")
        };

    // ── AlterColumn ──────────────────────────────────────────────────────────

    private static MigrationOperation ParseAlterColumn(
        Dictionary<string, string> rawArgs, MemberAccessExpressionSyntax memberAccess)
    {
        var sqlType = rawArgs.GetValueOrDefault("type");
        if (sqlType is null && memberAccess.Name is GenericNameSyntax gen)
            sqlType = gen.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();

        var nullableStr = rawArgs.GetValueOrDefault("nullable");
        bool? nullable = nullableStr is null ? null : nullableStr.Equals("true", StringComparison.OrdinalIgnoreCase);

        return new MigrationOperation
        {
            Type = "AlterColumn",
            TableName = rawArgs.GetValueOrDefault("table"),
            ColumnName = rawArgs.GetValueOrDefault("name"),
            ColumnType = sqlType,
            Nullable = nullable,
            DefaultValue = rawArgs.TryGetValue("defaultValue", out var dv) ? dv : null
        };
    }

    // ── CreateIndex / DropIndex ───────────────────────────────────────────────

    private static MigrationOperation ParseCreateIndex(
        Dictionary<string, string> rawArgs, InvocationExpressionSyntax invocation)
    {
        var columns = ExtractColumnOrColumns(invocation);

        var uniqueStr = rawArgs.GetValueOrDefault("unique");
        bool? isUnique = uniqueStr?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ? true : null;

        return new MigrationOperation
        {
            Type = "CreateIndex",
            TableName = rawArgs.GetValueOrDefault("table"),
            IndexName = rawArgs.GetValueOrDefault("name"),
            IndexColumns = columns.Count > 0 ? columns : null,
            IsUnique = isUnique
        };
    }

    private static MigrationOperation ParseDropIndex(Dictionary<string, string> rawArgs) =>
        new()
        {
            Type = "DropIndex",
            TableName = rawArgs.GetValueOrDefault("table"),
            IndexName = rawArgs.GetValueOrDefault("name")
        };

    // ── RenameColumn / RenameTable ────────────────────────────────────────────

    private static MigrationOperation ParseRenameColumn(Dictionary<string, string> rawArgs) =>
        new()
        {
            Type = "RenameColumn",
            TableName = rawArgs.GetValueOrDefault("table"),
            ColumnName = rawArgs.GetValueOrDefault("name"),
            NewName = rawArgs.GetValueOrDefault("newName")
        };

    private static MigrationOperation ParseRenameTable(Dictionary<string, string> rawArgs) =>
        new()
        {
            Type = "RenameTable",
            TableName = rawArgs.GetValueOrDefault("name"),
            NewName = rawArgs.GetValueOrDefault("newName"),
            Schema = rawArgs.TryGetValue("schema", out var s) ? s : null,
            NewSchema = rawArgs.TryGetValue("newSchema", out var ns) ? ns : null
        };

    // ── AddForeignKey / DropForeignKey ────────────────────────────────────────

    private static MigrationOperation ParseAddForeignKey(
        Dictionary<string, string> rawArgs, InvocationExpressionSyntax invocation)
    {
        // EF uses column: (singular) for single-column FKs
        var columnArg = invocation.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "column");
        var columnName = columnArg is not null
            ? ExtractLiteralValue(columnArg.Expression)
            : rawArgs.GetValueOrDefault("column");

        return new MigrationOperation
        {
            Type = "AddForeignKey",
            ForeignKeyName = rawArgs.GetValueOrDefault("name"),
            TableName = rawArgs.GetValueOrDefault("table"),
            ColumnName = columnName,
            PrincipalTable = rawArgs.GetValueOrDefault("principalTable"),
            PrincipalColumn = rawArgs.GetValueOrDefault("principalColumn"),
            OnDelete = rawArgs.TryGetValue("onDelete", out var od) ? od : null
        };
    }

    private static MigrationOperation ParseDropForeignKey(Dictionary<string, string> rawArgs) =>
        new()
        {
            Type = "DropForeignKey",
            ForeignKeyName = rawArgs.GetValueOrDefault("name"),
            TableName = rawArgs.GetValueOrDefault("table")
        };

    // ── AddPrimaryKey / DropPrimaryKey ────────────────────────────────────────

    private static MigrationOperation ParseAddPrimaryKey(
        Dictionary<string, string> rawArgs, InvocationExpressionSyntax invocation)
    {
        var columns = ExtractColumnOrColumns(invocation);
        return new MigrationOperation
        {
            Type = "AddPrimaryKey",
            TableName = rawArgs.GetValueOrDefault("table"),
            IndexName = rawArgs.GetValueOrDefault("name"),
            IndexColumns = columns.Count > 0 ? columns : null
        };
    }

    private static MigrationOperation ParseDropPrimaryKey(Dictionary<string, string> rawArgs) =>
        new()
        {
            Type = "DropPrimaryKey",
            TableName = rawArgs.GetValueOrDefault("table"),
            IndexName = rawArgs.GetValueOrDefault("name")
        };

    // ── Sql ───────────────────────────────────────────────────────────────────

    private static MigrationOperation ParseSql(InvocationExpressionSyntax invocation)
    {
        var sqlArg = invocation.ArgumentList.Arguments.FirstOrDefault();
        string? sqlText = sqlArg is not null ? ExtractLiteralValue(sqlArg.Expression) : null;

        string? sqlPreview = null;
        if (sqlText is not null)
        {
            const int maxLen = 200;
            sqlPreview = sqlText.Length > maxLen ? sqlText[..maxLen] + "..." : sqlText;
        }

        return new MigrationOperation { Type = "Sql", SqlPreview = sqlPreview };
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static List<string> ExtractColumnOrColumns(InvocationExpressionSyntax invocation)
    {
        var multiArg = invocation.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "columns");
        var singleArg = invocation.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "column");

        if (multiArg is not null) return ExtractStringArray(multiArg.Expression);
        if (singleArg is not null) return new List<string> { ExtractLiteralValue(singleArg.Expression) };
        return new List<string>();
    }

    private static List<string> ExtractStringArray(ExpressionSyntax expr)
    {
        InitializerExpressionSyntax? init = expr switch
        {
            ImplicitArrayCreationExpressionSyntax imp => imp.Initializer,
            ArrayCreationExpressionSyntax arr => arr.Initializer,
            _ => null
        };

        if (init is null) return new List<string>();

        return init.Expressions
            .OfType<LiteralExpressionSyntax>()
            .Select(lit => lit.Token.ValueText)
            .ToList();
    }

    private static Dictionary<string, string> ExtractNamedArgs(ArgumentListSyntax argList)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in argList.Arguments)
        {
            if (arg.NameColon?.Name.Identifier.Text is { } argName)
                result[argName] = ExtractLiteralValue(arg.Expression);
        }
        return result;
    }

    private static string ExtractLiteralValue(ExpressionSyntax expr)
    {
        return expr switch
        {
            LiteralExpressionSyntax lit => lit.Token.ValueText,
            PrefixUnaryExpressionSyntax neg => "-" + ExtractLiteralValue(neg.Operand),
            TypeOfExpressionSyntax typeOf => typeOf.Type.ToString(),
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => expr.ToString()
        };
    }

    // ── Symbol helpers (same pattern as other finders) ────────────────────────

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
