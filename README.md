# dotnet-context-mcp

An MCP (Model Context Protocol) server that gives AI assistants deep context about .NET codebases via Roslyn.

> **Status:** Early development — echo tool is live; Roslyn analysis tools are planned.

## What it does

Currently ships one tool:

| Tool | Description |
|------|-------------|
| `echo` | Reflects any message back to the caller — used to verify the server is wired up correctly |

Planned tools (Roslyn-powered):

- `get_diagnostics` — compile errors and warnings for a solution or project
- `get_symbols` — classes, methods, and members from source
- `get_references` — find all call sites for a symbol
- `get_hover` — XML-doc summaries and type info at a position

## Architecture

```
Claude Code (MCP client)
        │  stdio
        ▼
TypeScript MCP server   ← this repo (Node.js + @modelcontextprotocol/sdk)
        │  child_process / CLI flags
        ▼
.NET CLI bridge         ← planned (Roslyn workspace, outputs JSON on stdout)
```

The TypeScript layer handles the MCP protocol; the .NET layer owns Roslyn so there is no need to embed C# in Node.js.

## Installation

**Prerequisites:** Node.js ≥ 18, npm, Claude Code CLI.

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
Use the echo tool from dotnet-context-mcp to echo "hello world"
```

Expected response:

```
Echo from dotnet-context-mcp: hello world
```

## Roadmap

- [x] Project scaffold (TypeScript + MCP SDK)
- [x] `echo` tool — sanity-check the stdio transport
- [ ] .NET CLI bridge skeleton
- [ ] `get_diagnostics` tool
- [ ] `get_symbols` tool
- [ ] `get_references` tool
- [ ] `get_hover` tool

## License

MIT — see [LICENSE](LICENSE).
