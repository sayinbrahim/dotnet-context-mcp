# Changelog

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
