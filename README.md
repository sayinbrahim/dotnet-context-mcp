# dotnet-context-mcp

An MCP (Model Context Protocol) server that gives AI assistants deep context about .NET codebases via Roslyn.

## What it does

| Tool | Description |
|------|-------------|
| `echo` | Reflects any message back to the caller — used to verify the server is wired up correctly |
| `list_dbcontexts` | Lists all EF Core `DbContext` classes in a `.sln` file using Roslyn |

## Tools

### `echo`

Echoes a message back to the caller. Used for connectivity testing.

**Input**
| Field | Type | Description |
|-------|------|-------------|
| `message` | string | Text to echo back |

**Output** — plain text: `Echo from dotnet-context-mcp: <message>`

---

### `list_dbcontexts`

Lists all EF Core `DbContext` classes found in a .NET solution. Uses Roslyn to analyze the solution and returns class names, namespaces, project names, and file paths.

**Input**
| Field | Type | Description |
|-------|------|-------------|
| `solutionPath` | string | Absolute path to the `.sln` file to analyze |

**Output** — JSON with the following shape:

```json
{
  "solution": "MyApp.sln",
  "solutionPath": "C:\\path\\to\\MyApp.sln",
  "dbContexts": [
    {
      "name": "AppDbContext",
      "namespace": "MyApp.Data",
      "projectName": "MyApp.Data",
      "filePath": "MyApp.Data\\AppDbContext.cs"
    }
  ],
  "stats": {
    "projectsScanned": 3,
    "projectsWithEfCore": 1,
    "dbContextsFound": 1
  }
}
```

## Architecture

```
Claude Code (MCP client)
        │  stdio
        ▼
TypeScript MCP server   ← src/index.ts (Node.js + @modelcontextprotocol/sdk)
        │  child_process spawn + JSON on stdout
        ▼
.NET CLI bridge         ← cli/DotnetContextMcp.Cli/ (Roslyn workspace)
```

Both layers are in this repo. The TypeScript layer handles the MCP protocol; the .NET layer owns Roslyn so there is no need to embed C# in Node.js.

## Installation

**Prerequisites:** Node.js ≥ 18, npm, .NET 8 SDK or later, Claude Code CLI.

```bash
git clone https://github.com/sayinbrahim/dotnet-context-mcp.git
cd dotnet-context-mcp
npm install
npm run build
```

Register the server in your Claude Code config (`claude_desktop_config.json` or `.claude/settings.json`):

```json
{
  "mcpServers": {
    "dotnet-context-mcp": {
      "command": "node",
      "args": ["/absolute/path/to/dotnet-context-mcp/build/index.js"]
    }
  }
}
```

**Restart Claude Code** after saving the config so the new server is picked up.

## Usage

Once the server is running, ask Claude Code:

```
Use list_dbcontexts from dotnet-context-mcp with solutionPath C:\path\to\MyApp.sln
```

Or test connectivity:

```
Use the echo tool from dotnet-context-mcp to echo "hello world"
```

## Development

Build the TypeScript MCP layer:

```bash
npm run build
```

Build the .NET CLI:

```bash
dotnet build cli/DotnetContextMcp.Cli/
```

Run the .NET CLI directly (useful for testing without Claude Code):

```bash
dotnet run --project cli/DotnetContextMcp.Cli/ -- list-dbcontexts /path/to/MyApp.sln
```

## Roadmap

- [x] Project scaffold (TypeScript + MCP SDK)
- [x] `echo` tool — sanity-check the stdio transport
- [x] .NET CLI bridge skeleton (`cli/DotnetContextMcp.Cli/`)
- [x] `list_dbcontexts` — Roslyn-based EF Core DbContext discovery wired to MCP
- [ ] `get_dbcontext_schema` — return full entity/property model for a given DbContext
- [ ] `list_migrations` — enumerate applied and pending EF Core migrations
- [ ] `get_diagnostics` tool
- [ ] `get_symbols` tool

## Known Limitations

- **Cold start**: The first `dotnet run` call takes 10–20 seconds while the CLI project compiles. Subsequent calls within the same process are faster.
- **.NET 10 `.slnx` format not supported**: Only the classic `.sln` format is recognized. `.slnx` (SDK-style solution files introduced in .NET 10) will cause an error.
- **Non-ASCII paths**: Solution paths containing non-ASCII characters may cause issues depending on the system locale and .NET runtime version.

## Tested With

- `TestApp.sln` — synthetic solution with project `TestApp.Data` containing 2 DbContext classes (`TestDbContext`, `SecondDbContext`). Verified end-to-end via Claude Code on 2026-06-27.

## License

MIT — see [LICENSE](LICENSE).
