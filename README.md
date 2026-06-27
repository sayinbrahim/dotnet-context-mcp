# dotnet-context-mcp

[![Build](https://github.com/sayinbrahim/dotnet-context-mcp/actions/workflows/build.yml/badge.svg)](https://github.com/sayinbrahim/dotnet-context-mcp/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Node](https://img.shields.io/badge/node-%3E%3D18-green.svg)](https://nodejs.org)

Bring deep .NET solution context to Claude Code via Roslyn-powered MCP tools.

> **Status**: Early but functional. 4 tools working end-to-end. Looking for early adopters who want a better Claude Code experience on .NET projects.

## What it does

When Claude Code works on .NET projects, it reads files one at a time and infers structure. For larger solutions, this is slow and lossy.

This MCP server gives Claude **structured, Roslyn-backed access** to solution-level information. Questions like "what DbContexts exist", "show me entities in OrderContext", or "list migrations for ApplicationDbContext" become single tool calls instead of multi-file searches.

## Architecture

Two-layer bridge MCP design:

- **TypeScript MCP layer** (~300 lines): Handles MCP protocol, spawns subprocess, returns structured responses
- **.NET CLI layer** (~600 lines C#): Uses Roslyn Workspace API to load solutions and analyze code symbolically

```
Claude Code (MCP client)
        │  stdio
        ▼
TypeScript MCP server   ← src/index.ts (Node.js + @modelcontextprotocol/sdk)
        │  child_process spawn + JSON on stdout
        ▼
.NET CLI bridge         ← cli/DotnetContextMcp.Cli/ (Roslyn workspace)
```

## Available tools

### `echo`
Test tool to verify MCP connection. Echoes input back to the caller.

### `list_dbcontexts`
Lists all EF Core DbContext classes in a solution.
- **Input**: `solutionPath` (absolute .sln path)
- **Output**: DbContext name, namespace, project, file path
- Filters out abstract DbContexts automatically
- Skips projects without EF Core reference

### `list_entities`
Lists EF Core entities (`DbSet<T>` properties) across all DbContexts.
- **Input**: `solutionPath`, optional `dbContextName` filter
- **Output**: Entity name, namespace, file path, owning DbSet property name

### `list_migrations`
Lists EF Core migrations organized by DbContext.
- **Input**: `solutionPath`, optional `dbContextName` filter
- **Output**: Migration ID, name, ISO 8601 timestamp, file path
- Migrations sorted by timestamp ASC
- Multi-DbContext subfolder layout supported (e.g., `Migrations/SecondDb/`)
- Stats: earliest/latest migration date, total counts

## Installation

### Prerequisites
- Node.js 18+
- .NET 8 SDK
- Claude Code 2.x

### Setup

```bash
git clone https://github.com/sayinbrahim/dotnet-context-mcp.git
cd dotnet-context-mcp
npm install
npm run build
```

Register with Claude Code:

```bash
claude mcp add dotnet-context-mcp -- node /absolute/path/to/dotnet-context-mcp/build/index.js
```

Replace `/absolute/path/to/` with your actual path. To register globally (all projects), add `-s user`:

```bash
claude mcp add dotnet-context-mcp -s user -- node /absolute/path/to/build/index.js
```

**Restart Claude Code** (start a new session). MCP servers are loaded at session start, so the tool will not appear in your current session.

Verify:

```bash
claude mcp list
# Should show: dotnet-context-mcp  ✔ Connected
```

## Usage examples

### Discovering DbContexts

> **You**: What DbContexts exist in my solution at C:\src\MyApp\MyApp.sln?
>
> **Claude**: [calls list_dbcontexts] Found 2 DbContexts: ApplicationDbContext (MyApp.Data, 3 entities) and AuditDbContext (MyApp.Audit, 1 entity).

### Exploring entities

> **You**: Show me the entities in ApplicationDbContext.
>
> **Claude**: [calls list_entities with filter] ApplicationDbContext manages 3 entities: User, Order, Product. All in MyApp.Data.Entities namespace.

### Reviewing migrations

> **You**: What's the migration history for ApplicationDbContext?
>
> **Claude**: [calls list_migrations] 5 migrations:
> 1. 20240115_InitialCreate (2024-01-15)
> 2. 20240201_AddOrderTotal (2024-02-01)
> 3. ...

## Known limitations

- **Cold start**: First call takes 10–20 seconds (`dotnet run` warmup). Subsequent calls are 5–10s. **Phase 12 will fix this** via `dotnet publish` self-contained binary (~1s response time target).
- **.NET 10 .slnx format**: MSBuildWorkspace requires classic `.sln`. `.slnx` (.NET 10 XML format) is not yet supported.
- **Non-ASCII paths**: Solution paths with non-ASCII characters may fail. Use ASCII-only paths for now.

## Roadmap

### Done
- [x] Phase 1–5: MCP scaffold, Roslyn integration, list_dbcontexts
- [x] Phase 6: list_entities with optional filtering
- [x] Phase 7: list_migrations with metadata extraction

### Next
- [ ] Phase 8: analyze_migration (single migration detail with operations)
- [ ] Phase 9: find_relationships (entity navigation properties)
- [ ] Phase 10: find_dbcontext_dependencies (DI graph)
- [ ] Phase 11: analyze_solution_health (overall report)
- [ ] Phase 12: `dotnet publish` for fast cold start
- [ ] Phase 13: Publish as npm package (`npx dotnet-context-mcp`)
- [ ] Phase 14: Documentation site
- [ ] Phase 15: VS Code extension installer

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, testing, and PR guidelines.

## Support

- [GitHub Issues](https://github.com/sayinbrahim/dotnet-context-mcp/issues) for bugs
- [Discussions](https://github.com/sayinbrahim/dotnet-context-mcp/discussions) for questions

## License

MIT — see [LICENSE](LICENSE) for details.

## Author

Built by [Halil İbrahim Sayın](https://github.com/sayinbrahim) — Senior .NET Developer at Enerjisa, Ankara.  
Contact: [LinkedIn](https://www.linkedin.com/in/halil-ibrahim-say%C4%B1n-0a8760115/)
