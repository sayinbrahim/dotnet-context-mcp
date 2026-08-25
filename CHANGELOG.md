# Changelog

## v0.4.0 (unreleased)

### Added
- **HTTP transport (experimental)** — the MCP server can now run as an HTTP endpoint via `--transport http --port <n>`. Implements MCP 2026-07-28 spec's stateless request/response model (`src/transports/http-transport.ts`, built on the SDK's `WebStandardStreamableHTTPServerTransport`). Prep work for hosted deployment. No auth yet; localhost use only.

### Fixed
- `serverInfo.version` reported in the MCP `initialize` response was hardcoded to `"0.1.0"` and had drifted from the actual package version since v0.1.0. Now read dynamically from `package.json` at startup, so it can't drift again.

### Notes
- Stdio transport remains the default and is unchanged.
- Both transports share the same 11 tool implementations — no duplication.
- HTTP transport is stateless: a fresh transport is connected per request and closed after the response is written, since the SDK forbids reusing a stateless transport instance across requests. Known limitation: concurrent requests can race on the shared `McpServer` connection (`"Already connected to a transport"`); safe for local dev, not yet for concurrent production load. A transport pool is planned before real hosted deployment.

## v0.3.0 (2026-08-24)

### Added
- **New MCP tool: `analyze_migration_safety`** — runs 9 EF Core migration safety analyzers against a single migration file. Returns EFMS-coded issues (EFMS001-EFMS009) with severity, line numbers, messages, and recommendations. Line numbers only, no column positions.
- **New MCP tool: `get_solution_safety_report`** — aggregates safety analysis across every migration in a .NET solution. Returns safety score (0-100), grade (A-F), and top issues grouped by DbContext.

### Changed
- CLI now depends on `EfMigrationSafety.Analyzers` v0.2.0 (via NuGet PackageReference). No code duplication; both packages evolve together.

### Notes
- Total MCP tools: 11 (was 9).
- EFMS code mapping is defined in-CLI for now (`MigrationSafetyAnalyzer.cs`); a future ef-migration-safety release may expose these codes directly on the SafetyIssue record.

## v0.2.1 (2026-07-17)

### Added
- **CLI command: `init-client`** — zero-config MCP client setup from the terminal:
  - Auto-detects installed MCP clients (Claude Code, Cursor, Continue.dev, Windsurf)
  - Adds dotnet-context-mcp to their config with automatic backup
  - Interactive multi-client selection or --client flag for direct install
  - --remove, --verify, --yes flags for uninstall, status check, and CI usage
- Companion to the [VS Code / Cursor extension](https://open-vsx.org/extension/sayinbrahim/dotnet-context-mcp)
  now live on Open VSX

### Fixed
- CLI dispatch for `analyze-solution-health` and related commands now returns
  JSON to stdout instead of starting the MCP server (allows extension and
  scripts to consume output)
- Windows `spawn('npx', ...)` compatibility — uses shell: true on win32 to
  correctly resolve npx.cmd

### Docs
- Consolidated "Zero-Config Setup" section under Installation
- Added Open VSX extension link
- Marked Phase 13 as complete in roadmap

## v0.2.0 (2026-07-10)

### Added
- **Custom analyzer plugin system** — users can now define their own
  rules via JSON configuration files:
  - Discovery from both convention folder (`.dotnet-context-mcp/plugins/*.json`)
    and config file references (`dotnet-context-mcp.config.json`)
  - Three rule types supported in this release:
    - `name-regex` — enforces naming conventions on DbContexts or entities
      via regular expressions
    - `entity-count` — enforces complexity constraints on DbContext entity
      counts (less-than, less-than-or-equal, greater-than, etc.)
    - `operation-forbidden` — flags forbidden migration operation types
      (e.g., DropTable) in Up methods
  - Message templates with placeholders: `{name}`, `{count}`, `{migrationName}`,
    `{tableName}`, `{operationType}`
  - Rule validation at load time (invalid targets, invalid regex,
    missing required fields all reported without silent drops)
- New MCP tool: `run_custom_analyzers` — runs only custom rules, returns
  issue list
- Extended MCP tool: `analyze_solution_health` now accepts optional
  `include_custom` flag — merges custom rule issues into unified health
  report with recalculated score
- Total tools now: 9

### Notes
- DLL-based plugins (Roslyn analyzer assemblies) deferred to a future
  release — v0.2.0 focuses on JSON-based rules
- Additional rule types (property-exists, attribute-present) also
  deferred for feedback-driven prioritization

## v0.1.5 (2026-07-08)

### Added
- `analyze_solution_health` tool — aggregate health report for .NET solutions:
  - Composes 5 underlying analyzers (DbContext, Entity, Migration, Relationship, Registration)
  - Detects 5 issue categories: multi-context-registration, hardcoded-connection-string, no-registration, no-migrations, many-manytomany
  - Health score (0-100) with grade (A-F)
  - Actionable recommendations grouped by category
  - Per-DbContext breakdown
- Total tools now: 8

### Fixed
- DbContext deduplication at the health analyzer layer — prevents
  double-counting when a DbContext class library is referenced by
  multiple projects (a common pattern in web + data library setups).
  Note: individual `list_dbcontexts` still returns per-project entries
  unchanged.

## v0.1.4 (2026-07-06)

### Added
- `find_dbcontext_dependencies` tool — analyzes DI registration of
  DbContexts across the solution:
  - Detects `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` calls
  - Provider inference (SqlServer, Npgsql, Sqlite, InMemory, Oracle, MySQL, Cosmos)
  - Connection string source (Configuration, Hardcoded, EnvironmentVariable)
  - Lifetime (Scoped for pool, Singleton for factory)
  - Location tracking (file + line number, solution-relative paths)
- Total tools now: 7

## v0.1.3 (2026-07-04)

### Changed
- Documentation refresh:
  - Updated tools table to reflect all 6 tools (added `analyze_migration` and `find_relationships` details)
  - Added Usage Examples section with concrete Claude Code prompts
  - Fixed broken CI badge (was referencing non-existent workflow)
  - Updated architecture diagram
  - Refreshed roadmap (Phase 8, 9 complete; Phase 10, 11, 12 planned)

### Notes
- No code changes in this release — same functionality as v0.1.2

## v0.1.2 (2026-07-03)

### Added
- `find_relationships` MCP tool — analyzes entity navigation
  properties and foreign keys, returns relationship graph with:
  - Detected relationship types (OneToMany, ManyToOne, OneToOne, ManyToMany)
  - Foreign key property names
  - Required/optional flag from nullability + [Required] attribute
  - Cascade behavior when available
  - Detection source tracking (Navigation / Convention / DataAnnotation / FluentApi)
- Convention-based FK detection (naming pattern like "UserId" → User)
- Fluent API parsing in OnModelCreating (HasOne/HasMany chains)
- Data annotation support ([ForeignKey], [InverseProperty])
- `--entity` filter for scoped queries

## v0.1.1 (2026-06-29)

### Added
- `analyze_migration` tool — returns detailed Up/Down operations for a specific migration including:
  - CreateTable, DropTable with full column metadata (name, type, nullable, isPrimaryKey)
  - AddColumn, DropColumn, AlterColumn
  - CreateIndex, DropIndex (including unique indexes)
  - AddForeignKey, DropForeignKey with ReferentialAction (onDelete)
  - AddPrimaryKey, DropPrimaryKey
  - RenameColumn, RenameTable
  - Raw Sql execution (truncated preview)
- Roslyn syntax-level analysis (parses Up()/Down() method bodies via SyntaxNode walking)
- Anonymous object parsing for CreateTable columns lambda
- Primary key detection from constraints lambda
- Operation summary with per-type counts (tablesCreated, columnsAdded, indexesCreated, etc.)

### Notes
- When .NET CLI source changes locally, run `npm run build:cli` to refresh the published binary.
  The TypeScript layer prefers the published binary over `dotnet run` for performance.

## v0.1.0 (2026-06-28)

### Added
- Initial release with 4 MCP tools: echo, list_dbcontexts, list_entities, list_migrations
- Bridge MCP architecture (TypeScript MCP server + .NET CLI for Roslyn analysis)
- Cross-platform binaries (win-x64, linux-x64, osx-x64, osx-arm64)
- npm package with post-install platform binary download
