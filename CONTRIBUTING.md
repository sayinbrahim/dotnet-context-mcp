# Contributing to dotnet-context-mcp

Thank you for your interest!

## Development setup

Prerequisites:
- Node.js 18+
- .NET 8 SDK
- Git

```bash
git clone https://github.com/sayinbrahim/dotnet-context-mcp.git
cd dotnet-context-mcp
npm install
npm run build
dotnet build cli/DotnetContextMcp.Cli/
```

## Testing locally

### Via MCP Inspector

```bash
npx @modelcontextprotocol/inspector node build/index.js
```

Open the URL it prints, click Connect, navigate to Tools, test each tool with a real `.sln` path.

### Via Claude Code

Register the server as `local` for this project:

```bash
claude mcp add dotnet-context-mcp -- node $(pwd)/build/index.js
```

Restart Claude Code session, then call tools.

## Code style

- **TypeScript**: ES modules, strict mode, no `any`
- **C#**: .NET 8 conventions, file-scoped namespaces, records for DTOs
- **Imports**: TypeScript imports use `.js` extension (ES module requirement)
- **stderr/stdout**: CLI logs go to stderr, JSON output goes to stdout (critical for subprocess use)

## Adding a new tool

1. Add analyzer logic in `cli/DotnetContextMcp.Cli/Analysis/`
2. Add CLI subcommand in `cli/DotnetContextMcp.Cli/Program.cs`
3. Test CLI directly: `dotnet run --project cli/DotnetContextMcp.Cli/ -- your-command <args>`
4. Add MCP tool registration in `src/index.ts` (use existing tools as template)
5. Build TypeScript: `npm run build`
6. Test via Inspector, then via Claude Code (with session restart)

## Pull requests

- One feature per PR
- Include CLI test results in PR description
- Update README if adding/modifying tools
- Tag commits with the phase they implement (e.g., "Phase 8: analyze_migration")
