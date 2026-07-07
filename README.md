# dotnet-context-mcp

[![npm version](https://img.shields.io/npm/v/dotnet-context-mcp.svg)](https://www.npmjs.com/package/dotnet-context-mcp)
[![npm downloads](https://img.shields.io/npm/dt/dotnet-context-mcp.svg)](https://www.npmjs.com/package/dotnet-context-mcp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![GitHub Release](https://img.shields.io/github/v/release/sayinbrahim/dotnet-context-mcp)](https://github.com/sayinbrahim/dotnet-context-mcp/releases)
[![Release](https://github.com/sayinbrahim/dotnet-context-mcp/actions/workflows/release.yml/badge.svg)](https://github.com/sayinbrahim/dotnet-context-mcp/actions/workflows/release.yml)

A solution-aware MCP server for .NET — eight Roslyn-powered tools that give Claude Code structured, symbol-level access to your DbContexts, entities, migrations, relationships, DI registrations, and aggregate health analysis. Published on npm.

> **Status**: v0.1.5 published to npm. 8 tools live. Looking for early adopters and real-world feedback from teams running multi-context EF Core projects. Issues and PRs welcome.

## What it does

When Claude Code works on .NET projects, it reads files one at a time and infers structure. For larger solutions, this is slow and lossy.

This MCP server gives Claude **structured, Roslyn-backed access** to solution-level information. Questions like "what DbContexts exist", "what does this migration actually do", or "how are User and Order related" become single tool calls instead of multi-file searches.

## Architecture

Two-layer bridge MCP design:

- **TypeScript MCP layer** (~270 lines): Handles MCP protocol, spawns subprocess, returns structured responses
- **.NET CLI layer** (~2,000 lines C#): Uses the Roslyn Workspace API to load solutions and analyze code symbolically

```
Claude Code (MCP client)
       │  stdio
       ▼
TypeScript MCP Server (src/index.ts)
       │  spawns
       ▼
.NET CLI (cli/DotnetContextMcp.Cli) — published binary or dotnet run
       │  uses
       ▼
Roslyn (Microsoft.CodeAnalysis) — loads .sln, analyzes symbols/syntax
       │  reads
       ▼
Your .NET solution (DbContexts, entities, migrations)
```

## Available tools

| Tool | Description |
|------|-------------|
| `echo` | Connectivity test |
| `list_dbcontexts` | Discover all DbContext classes in the solution |
| `list_entities` | List DbSet properties and entity metadata (optionally filtered by DbContext) |
| `list_migrations` | Migration history with timestamps and owning context |
| `analyze_migration` | Detailed Up/Down operations for a specific migration (13 operation types) |
| `find_relationships` | Entity relationships: navigation properties, foreign keys, cardinality (OneToMany, ManyToOne, OneToOne, ManyToMany) |
| `find_dbcontext_dependencies` | Analyze DbContext dependency injection registrations across the solution: registration method (AddDbContext, AddDbContextPool, AddDbContextFactory), provider (SqlServer, Npgsql, Sqlite, etc.), connection string source, lifetime, and location (file + line). |
| `analyze_solution_health` | Comprehensive EF Core health report for the solution. Composes all other analyzers into an aggregate view: DbContext + entity + migration + registration + relationship counts. Detects 5 issue categories (multi-context registration, hardcoded connection strings, unregistered DbContexts, missing migrations, many-to-many complexity). Returns health score (0-100), grade (A-F), and actionable recommendations. |

## Platform support

| Platform | RID | Status |
|---|---|---|
| Windows x64 | `win-x64` | Tested |
| Linux x64 | `linux-x64` | Binary built, untested in production |
| macOS x64 (Intel) | `osx-x64` | Binary built, untested in production |
| macOS ARM64 (Apple Silicon) | `osx-arm64` | Binary built, untested in production |

**Current limitation:** `Microsoft.Build.Locator` requires a .NET 8 SDK to be installed on the target machine to locate MSBuild. Truly hermetic operation (no SDK required) is deferred to a future phase.

## Installation

### Quick install (recommended)

```bash
claude mcp add dotnet-context-mcp -s user -- npx -y dotnet-context-mcp@latest
```

The `-s user` flag makes this MCP server available globally across all your projects. For project-specific install, omit the flag.

Restart Claude Code (`/exit`, then `claude`) to load the new MCP server.

Verify the server is connected:

```bash
claude mcp list
# Should show: dotnet-context-mcp  ✔ Connected
```

The first tool call may take 5-15 seconds (npm fetches the package and downloads the platform-specific binary). Subsequent calls are 3-4 seconds.

### Platform support

Pre-built binaries are automatically downloaded for:
- Windows x64
- Linux x64
- macOS Intel (x64)
- macOS Apple Silicon (arm64)

### Requirements

- Node.js 18+
- Claude Code 2.x
- .NET 8 SDK installed on your machine (required for Roslyn's MSBuildLocator)
- `tar` command (built-in on macOS/Linux; available on Windows 10+)

### From source (developers)

```bash
git clone https://github.com/sayinbrahim/dotnet-context-mcp.git
cd dotnet-context-mcp
npm install
npm run build:all  # TypeScript + Windows binary
claude mcp add dotnet-context-mcp -- node /absolute/path/to/build/index.js
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup details.

## Usage examples

After installation, just ask Claude Code in plain language:

### Discovering DbContexts

> "List all DbContexts in this solution: C:\path\to\MySolution.sln"

Claude calls `list_dbcontexts` and returns each DbContext's name, namespace, project, and file path.

### Analyzing a migration

> "Analyze the AddOrderRelations migration and tell me if it's safe to deploy"

Claude calls `analyze_migration`, inspects the Up/Down operations (CreateTable, AddForeignKey, AlterColumn, etc.), and gives you an informed answer — not just that a migration exists, but what it actually does.

### Mapping entity relationships

> "What's the relationship between User and Order in TestDbContext?"

Claude calls `find_relationships` and returns the navigation graph: cardinality (OneToMany/ManyToOne), the foreign key column, and whether the relationship is required.

## Known limitations

- **Cold start**: First call takes 3–4 seconds (self-contained binary warm-up). Previously 15–20s with `dotnet run`.
- **.NET SDK required**: `MSBuildLocator` must find a .NET 8 SDK on the target machine. Hermetic operation is a future phase.
- **.NET 10 .slnx format**: MSBuildWorkspace requires classic `.sln`. `.slnx` (.NET 10 XML format) is not yet supported.
- **Non-ASCII paths**: Solution paths with non-ASCII characters may fail. Use ASCII-only paths for now.

## Roadmap

- [x] Phase 1-5: MCP scaffold + Roslyn (list_dbcontexts)
- [x] Phase 6: list_entities
- [x] Phase 7: list_migrations
- [x] Phase 8: analyze_migration (v0.1.1)
- [x] Phase 9: find_relationships (v0.1.2)
- [x] Phase 10: find_dbcontext_dependencies (v0.1.4)
- [x] Phase 11: analyze_solution_health (v0.1.5)
- [ ] Phase 12: analyzer plugins / custom rules
- [ ] Phase 13: VS Code extension installer

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
