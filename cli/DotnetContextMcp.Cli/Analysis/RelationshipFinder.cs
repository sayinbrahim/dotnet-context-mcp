using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetContextMcp.Cli.Analysis;

public record RelationshipInfo(
    string SourceEntity,
    string SourceProperty,
    string TargetEntity,
    string RelationshipType,   // "OneToMany", "ManyToOne", "OneToOne", "ManyToMany"
    string? ForeignKeyProperty,
    bool IsRequired,
    string? OnDelete,
    string DetectionSource     // "Navigation" | "Convention" | "DataAnnotation" | "FluentApi"
);

public class RelationshipFinder
{
    public int EntitiesAnalyzed { get; private set; }

    public List<RelationshipInfo> Find(Solution solution, string dbContextName, string? entityName = null)
    {
        var (relationships, entitiesAnalyzed) = FindAsync(solution, dbContextName).GetAwaiter().GetResult();
        EntitiesAnalyzed = entitiesAnalyzed;

        if (entityName is not null)
            relationships = relationships.Where(r => r.SourceEntity == entityName).ToList();

        return relationships;
    }

    private static async Task<(List<RelationshipInfo> Relationships, int EntitiesAnalyzed)> FindAsync(Solution solution, string dbContextName)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null) continue;

            var dbContextBaseType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            if (dbContextBaseType is null) continue;

            var contextType = GetAllNamedTypes(compilation.GlobalNamespace)
                .FirstOrDefault(t => !t.IsAbstract && t.TypeKind == TypeKind.Class
                    && t.Name == dbContextName && InheritsFrom(t, dbContextBaseType));

            if (contextType is null) continue;

            var entityTypes = GetDbSetEntityTypes(contextType);
            if (entityTypes.Count == 0) return (new List<RelationshipInfo>(), 0);

            var fkMapsByEntity = entityTypes.ToDictionary(
                kv => kv.Key,
                kv => GetForeignKeyLikeProperties(kv.Value, entityTypes));

            var requiredAttrType = compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.RequiredAttribute");
            var foreignKeyAttrType = compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.Schema.ForeignKeyAttribute");

            var builders = CollectNavigationRelationships(entityTypes, fkMapsByEntity, requiredAttrType);
            ApplyDataAnnotations(entityTypes, builders, foreignKeyAttrType);
            await ApplyFluentApi(contextType, entityTypes, builders);
            InferRelationshipTypes(builders);

            var relationships = builders
                .OrderBy(b => b.SourceEntity, StringComparer.Ordinal)
                .ThenBy(b => b.SourceProperty, StringComparer.Ordinal)
                .Select(b => new RelationshipInfo(
                    b.SourceEntity, b.SourceProperty, b.TargetEntity, b.RelationshipType,
                    b.ForeignKeyProperty, b.IsRequired, b.OnDelete, b.DetectionSource))
                .ToList();

            return (relationships, entityTypes.Count);
        }

        return (new List<RelationshipInfo>(), 0);
    }

    // ── Navigation properties (Source 1) + convention FK attachment (Source 2) ─

    private sealed class RelBuilder
    {
        public required string SourceEntity;
        public required string SourceProperty;
        public required string TargetEntity;
        public required bool IsCollection;
        public string RelationshipType = "";
        public string? ForeignKeyProperty;
        public bool IsRequired;
        public string? OnDelete;
        public string DetectionSource = "Navigation";
    }

    private static List<RelBuilder> CollectNavigationRelationships(
        Dictionary<string, INamedTypeSymbol> entityTypes,
        Dictionary<string, Dictionary<string, string>> fkMapsByEntity,
        INamedTypeSymbol? requiredAttrType)
    {
        var results = new List<RelBuilder>();

        foreach (var (entityName, entitySymbol) in entityTypes)
        {
            foreach (var prop in entitySymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.IsStatic || prop.IsIndexer) continue;

                if (TryGetCollectionElementType(prop.Type, out var elementType) &&
                    elementType is INamedTypeSymbol namedElement &&
                    entityTypes.TryGetValue(namedElement.Name, out var collectionTarget) &&
                    SymbolEqualityComparer.Default.Equals(collectionTarget, namedElement))
                {
                    var fk = FindForeignKeyProperty(fkMapsByEntity[namedElement.Name], entityName, entityName);

                    results.Add(new RelBuilder
                    {
                        SourceEntity = entityName,
                        SourceProperty = prop.Name,
                        TargetEntity = namedElement.Name,
                        IsCollection = true,
                        RelationshipType = "OneToMany",
                        ForeignKeyProperty = fk,
                        IsRequired = IsRequiredNav(prop, requiredAttrType),
                        DetectionSource = "Navigation"
                    });
                    continue;
                }

                if (prop.Type is INamedTypeSymbol refType &&
                    entityTypes.TryGetValue(refType.Name, out var refTarget) &&
                    SymbolEqualityComparer.Default.Equals(refTarget, refType))
                {
                    var fk = FindForeignKeyProperty(fkMapsByEntity[entityName], prop.Name, refType.Name);

                    results.Add(new RelBuilder
                    {
                        SourceEntity = entityName,
                        SourceProperty = prop.Name,
                        TargetEntity = refType.Name,
                        IsCollection = false,
                        RelationshipType = "ManyToOne",
                        ForeignKeyProperty = fk,
                        IsRequired = IsRequiredNav(prop, requiredAttrType),
                        DetectionSource = "Navigation"
                    });
                }
            }
        }

        return results;
    }

    private static bool IsRequiredNav(IPropertySymbol prop, INamedTypeSymbol? requiredAttrType)
    {
        if (prop.NullableAnnotation != NullableAnnotation.Annotated) return true;
        if (requiredAttrType is not null &&
            prop.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, requiredAttrType)))
            return true;
        return false;
    }

    private static Dictionary<string, INamedTypeSymbol> GetDbSetEntityTypes(INamedTypeSymbol contextType)
    {
        var entityTypes = new Dictionary<string, INamedTypeSymbol>();
        foreach (var member in contextType.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.Type is not INamedTypeSymbol propType) continue;
            if (!propType.IsGenericType || propType.Name != "DbSet") continue;
            if (propType.ContainingNamespace?.ToDisplayString() != "Microsoft.EntityFrameworkCore") continue;
            if (propType.TypeArguments.Length == 0) continue;

            if (propType.TypeArguments[0] is INamedTypeSymbol entitySymbol)
                entityTypes[entitySymbol.Name] = entitySymbol;
        }
        return entityTypes;
    }

    private static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;
        if (type.SpecialType == SpecialType.System_String) return false;
        if (type is not INamedTypeSymbol named) return false;

        bool IsIEnumerableOfT(INamedTypeSymbol t) =>
            t.IsGenericType && t.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>";

        if (IsIEnumerableOfT(named))
        {
            elementType = named.TypeArguments[0];
            return true;
        }

        var ienum = named.AllInterfaces.FirstOrDefault(IsIEnumerableOfT);
        if (ienum is not null)
        {
            elementType = ienum.TypeArguments[0];
            return true;
        }

        return false;
    }

    // ── Convention-based FK matching (Source 2) ─────────────────────────────

    private static Dictionary<string, string> GetForeignKeyLikeProperties(
        INamedTypeSymbol entity, Dictionary<string, INamedTypeSymbol> entityTypes)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in entity.GetMembers().OfType<IPropertySymbol>())
        {
            if (prop.Name == "Id" || !prop.Name.EndsWith("Id", StringComparison.Ordinal)) continue;
            if (!IsForeignKeyIdType(prop.Type)) continue;

            var prefix = prop.Name[..^2];
            if (entityTypes.ContainsKey(prefix))
                map[prefix] = prop.Name;
        }
        return map;
    }

    private static bool IsForeignKeyIdType(ITypeSymbol type)
    {
        var t = type;
        if (t is INamedTypeSymbol { Name: "Nullable", IsGenericType: true } nullable)
            t = nullable.TypeArguments[0];

        if (t.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_String)
            return true;

        return t.Name == "Guid" && t.ContainingNamespace?.ToDisplayString() == "System";
    }

    private static string? FindForeignKeyProperty(Dictionary<string, string> fkMap, string preferredKey, string fallbackKey)
    {
        if (fkMap.TryGetValue(preferredKey, out var byPreferred)) return byPreferred;
        if (fkMap.TryGetValue(fallbackKey, out var byFallback)) return byFallback;
        return null;
    }

    // ── Data annotations (Source 3) ─────────────────────────────────────────

    private static void ApplyDataAnnotations(
        Dictionary<string, INamedTypeSymbol> entityTypes,
        List<RelBuilder> builders,
        INamedTypeSymbol? foreignKeyAttrType)
    {
        if (foreignKeyAttrType is null) return;

        foreach (var (entityName, entitySymbol) in entityTypes)
        {
            foreach (var prop in entitySymbol.GetMembers().OfType<IPropertySymbol>())
            {
                var attr = prop.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, foreignKeyAttrType));
                if (attr is null || attr.ConstructorArguments.Length == 0) continue;
                if (attr.ConstructorArguments[0].Value is not string referencedName) continue;

                // [ForeignKey] on the navigation property: argument names the FK scalar property.
                var onNav = builders.FirstOrDefault(b =>
                    b.SourceEntity == entityName && b.SourceProperty == prop.Name && !b.IsCollection);
                if (onNav is not null)
                {
                    onNav.ForeignKeyProperty = referencedName;
                    onNav.DetectionSource = "DataAnnotation";
                    continue;
                }

                // [ForeignKey] on the FK scalar property: argument names the navigation property.
                var onFkProp = builders.FirstOrDefault(b =>
                    b.SourceEntity == entityName && b.SourceProperty == referencedName && !b.IsCollection);
                if (onFkProp is not null)
                {
                    onFkProp.ForeignKeyProperty = prop.Name;
                    onFkProp.DetectionSource = "DataAnnotation";
                }
            }
        }
    }

    // ── Fluent API in OnModelCreating (Source 4) ────────────────────────────

    private static async Task ApplyFluentApi(
        INamedTypeSymbol contextType,
        Dictionary<string, INamedTypeSymbol> entityTypes,
        List<RelBuilder> builders)
    {
        var onModelCreating = contextType.GetMembers("OnModelCreating").OfType<IMethodSymbol>().FirstOrDefault();
        if (onModelCreating is null) return;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var node = await syntaxRef.GetSyntaxAsync();
            if (node is not MethodDeclarationSyntax { Body: { } body, ParameterList.Parameters.Count: > 0 } method)
                continue;

            var modelBuilderParamName = method.ParameterList.Parameters[0].Identifier.Text;

            foreach (var statement in body.Statements)
            {
                if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
                    continue;

                ParseFluentChain(invocation, modelBuilderParamName, entityTypes, builders);
            }
        }
    }

    private static void ParseFluentChain(
        InvocationExpressionSyntax outermost,
        string modelBuilderParamName,
        Dictionary<string, INamedTypeSymbol> entityTypes,
        List<RelBuilder> builders)
    {
        var calls = FlattenChain(outermost);
        if (calls.Count == 0) return;

        var (rootReceiver, entityCallIndex) = FindEntityCall(calls, modelBuilderParamName);
        if (rootReceiver != modelBuilderParamName || entityCallIndex < 0) return;

        var entityCall = calls[entityCallIndex];
        var rootEntityName = entityCall.GenericArg;
        if (rootEntityName is null || !entityTypes.ContainsKey(rootEntityName)) return;

        string? navProp = null, inverseNavProp = null, fkProp = null, onDelete = null, targetEntityName = null;
        bool isHasOne = false, isHasMany = false;

        for (int i = entityCallIndex + 1; i < calls.Count; i++)
        {
            var call = calls[i];
            switch (call.Method)
            {
                case "HasOne":
                    isHasOne = true;
                    navProp = ExtractLambdaMemberName(call.Invocation);
                    targetEntityName ??= call.GenericArg;
                    break;
                case "HasMany":
                    isHasMany = true;
                    navProp = ExtractLambdaMemberName(call.Invocation);
                    targetEntityName ??= call.GenericArg;
                    break;
                case "WithOne":
                    inverseNavProp = ExtractLambdaMemberName(call.Invocation);
                    break;
                case "WithMany":
                    inverseNavProp = ExtractLambdaMemberName(call.Invocation);
                    break;
                case "HasForeignKey":
                    fkProp = ExtractLambdaMemberName(call.Invocation);
                    break;
                case "OnDelete":
                    onDelete = ExtractEnumMemberName(call.Invocation);
                    break;
            }
        }

        if (!isHasOne && !isHasMany) return;
        if (navProp is null) return;

        // Resolve the related entity from the nav property's declared type if no generic arg was given.
        targetEntityName ??= entityTypes.TryGetValue(rootEntityName, out var rootSym)
            ? ResolveNavTargetEntityName(rootSym, navProp, entityTypes)
            : null;
        if (targetEntityName is null || !entityTypes.ContainsKey(targetEntityName)) return;

        string rootRelType = isHasMany ? "OneToMany" : "ManyToOne";
        string inverseRelType = isHasMany ? "ManyToOne" : "OneToMany";

        var rootEntry = FindOrCreateEntry(builders, rootEntityName, navProp, targetEntityName, isHasMany);
        rootEntry.RelationshipType = rootRelType;
        rootEntry.DetectionSource = "FluentApi";

        RelBuilder? inverseEntry = null;
        if (inverseNavProp is not null)
        {
            inverseEntry = FindOrCreateEntry(builders, targetEntityName, inverseNavProp, rootEntityName, !isHasMany);
            inverseEntry.RelationshipType = inverseRelType;
            inverseEntry.DetectionSource = "FluentApi";
        }

        if (fkProp is not null)
        {
            // The FK scalar lives on the "many"/dependent side.
            var dependentEntry = isHasMany ? inverseEntry : rootEntry;
            if (dependentEntry is not null) dependentEntry.ForeignKeyProperty = fkProp;
        }

        if (onDelete is not null)
        {
            // Attach to the collection ("one") side, matching the shape used for convention-detected relationships.
            var collectionEntry = isHasMany ? rootEntry : inverseEntry;
            if (collectionEntry is not null) collectionEntry.OnDelete = onDelete;
        }
    }

    private static RelBuilder FindOrCreateEntry(
        List<RelBuilder> builders, string sourceEntity, string sourceProperty, string targetEntity, bool isCollection)
    {
        var existing = builders.FirstOrDefault(b =>
            b.SourceEntity == sourceEntity && b.SourceProperty == sourceProperty);
        if (existing is not null) return existing;

        var created = new RelBuilder
        {
            SourceEntity = sourceEntity,
            SourceProperty = sourceProperty,
            TargetEntity = targetEntity,
            IsCollection = isCollection
        };
        builders.Add(created);
        return created;
    }

    private static string? ResolveNavTargetEntityName(
        INamedTypeSymbol entity, string navPropertyName, Dictionary<string, INamedTypeSymbol> entityTypes)
    {
        var prop = entity.GetMembers(navPropertyName).OfType<IPropertySymbol>().FirstOrDefault();
        if (prop is null) return null;

        if (TryGetCollectionElementType(prop.Type, out var elementType) && elementType is INamedTypeSymbol named)
            return entityTypes.ContainsKey(named.Name) ? named.Name : null;

        if (prop.Type is INamedTypeSymbol refType && entityTypes.ContainsKey(refType.Name))
            return refType.Name;

        return null;
    }

    private record ChainCall(string Method, string? GenericArg, InvocationExpressionSyntax Invocation);

    private static List<ChainCall> FlattenChain(InvocationExpressionSyntax outermost)
    {
        var chain = new List<ChainCall>();

        void Walk(ExpressionSyntax expr)
        {
            if (expr is not InvocationExpressionSyntax inv || inv.Expression is not MemberAccessExpressionSyntax ma)
                return;

            Walk(ma.Expression);

            var methodName = ma.Name switch
            {
                GenericNameSyntax gen => gen.Identifier.Text,
                _ => ma.Name.Identifier.Text
            };
            var genericArg = ma.Name is GenericNameSyntax g
                ? g.TypeArgumentList.Arguments.FirstOrDefault()?.ToString()
                : null;

            chain.Add(new ChainCall(methodName, genericArg, inv));
        }

        Walk(outermost);
        return chain;
    }

    private static (string? Receiver, int EntityCallIndex) FindEntityCall(List<ChainCall> calls, string modelBuilderParamName)
    {
        var entityCallIndex = calls.FindIndex(c => c.Method == "Entity");
        if (entityCallIndex != 0) return (null, -1);

        var firstCallReceiver = calls[0].Invocation.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id }
            ? id.Identifier.Text
            : null;

        return (firstCallReceiver, entityCallIndex);
    }

    private static string? ExtractLambdaMemberName(InvocationExpressionSyntax invocation)
    {
        var lambda = invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .OfType<SimpleLambdaExpressionSyntax>()
            .FirstOrDefault();

        return lambda?.Body switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => null
        };
    }

    private static string? ExtractEnumMemberName(InvocationExpressionSyntax invocation)
    {
        var arg = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return arg switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => null
        };
    }

    // ── Relationship type inference (OneToOne / ManyToMany upgrade) ────────

    private static void InferRelationshipTypes(List<RelBuilder> builders)
    {
        var byPair = builders
            .GroupBy(b => (b.SourceEntity, b.TargetEntity))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var group in byPair)
        {
            var (source, target) = group.Key;
            if (!byPair.TryGetValue((target, source), out var inverseGroup)) continue;

            foreach (var forward in group.Value)
            {
                foreach (var backward in inverseGroup!)
                {
                    if (forward.IsCollection && !backward.IsCollection)
                    {
                        forward.RelationshipType = "OneToMany";
                        backward.RelationshipType = "ManyToOne";
                    }
                    else if (!forward.IsCollection && !backward.IsCollection)
                    {
                        forward.RelationshipType = "OneToOne";
                        backward.RelationshipType = "OneToOne";
                    }
                    else if (forward.IsCollection && backward.IsCollection)
                    {
                        forward.RelationshipType = "ManyToMany";
                        backward.RelationshipType = "ManyToMany";
                    }
                }
            }
        }
    }

    // ── Symbol helpers (same pattern as other finders) ──────────────────────

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
}
