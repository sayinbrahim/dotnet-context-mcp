import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { spawn } from "node:child_process";
import { resolve as resolvePath } from "node:path";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname } from "node:path";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

function resolveCliCommand(projectRoot: string): { command: string; baseArgs: string[]; cwd: string } {
  const platform = process.platform;
  const arch = process.arch;

  let rid: string;
  let exeName: string;

  if (platform === "win32") {
    rid = "win-x64";
    exeName = "DotnetContextMcp.Cli.exe";
  } else if (platform === "linux") {
    rid = "linux-x64";
    exeName = "DotnetContextMcp.Cli";
  } else if (platform === "darwin") {
    rid = arch === "arm64" ? "osx-arm64" : "osx-x64";
    exeName = "DotnetContextMcp.Cli";
  } else {
    throw new Error(`Unsupported platform: ${platform}-${arch}`);
  }

  const binaryPath = resolvePath(projectRoot, "build/cli", rid, exeName);

  if (existsSync(binaryPath)) {
    return {
      command: binaryPath,
      baseArgs: [],
      cwd: projectRoot,
    };
  }

  // Fallback: dotnet run (developer mode, slow but works without a published binary)
  console.error(
    `[dotnet-context-mcp] Binary not found at ${binaryPath}, falling back to 'dotnet run'. Run 'npm run build:cli' to build the binary for faster startup.`
  );
  const cliProjectPath = resolvePath(projectRoot, "cli/DotnetContextMcp.Cli");
  return {
    command: "dotnet",
    baseArgs: ["run", "--project", cliProjectPath, "--"],
    cwd: projectRoot,
  };
}

async function callCli(
  subcommand: string,
  subcommandArgs: string[],
  toolName: string
): Promise<{ content: Array<{ type: "text"; text: string }>; isError?: boolean }> {
  const projectRoot = resolvePath(__dirname, "..");
  const { command, baseArgs, cwd } = resolveCliCommand(projectRoot);
  const allArgs = [...baseArgs, subcommand, ...subcommandArgs];

  const result = await new Promise<{ stdout: string; stderr: string; exitCode: number }>(
    (resolveResult, rejectResult) => {
      const proc = spawn(command, allArgs, { cwd });
      let stdout = "";
      let stderr = "";
      proc.stdout.on("data", (chunk) => { stdout += chunk.toString(); });
      proc.stderr.on("data", (chunk) => { stderr += chunk.toString(); });
      proc.on("close", (code) => { resolveResult({ stdout, stderr, exitCode: code ?? -1 }); });
      proc.on("error", (err) => { rejectResult(err); });
    }
  );

  if (result.stderr) {
    console.error(`[${toolName}] CLI stderr:`, result.stderr);
  }

  if (result.exitCode !== 0) {
    return {
      content: [{ type: "text", text: `Error: .NET CLI exited with code ${result.exitCode}\n\nStderr:\n${result.stderr}` }],
      isError: true,
    };
  }

  try {
    const data = JSON.parse(result.stdout);
    return {
      content: [{ type: "text", text: JSON.stringify(data, null, 2) }],
    };
  } catch {
    return {
      content: [{ type: "text", text: `Error parsing CLI output as JSON:\n\nStdout:\n${result.stdout}\n\nStderr:\n${result.stderr}` }],
      isError: true,
    };
  }
}

const server = new McpServer({
  name: "dotnet-context-mcp",
  version: "0.1.0",
});

server.registerTool(
  "echo",
  {
    title: "Echo",
    description: "Echoes the input back to the caller",
    inputSchema: z.object({
      message: z.string().describe("Message to echo back"),
    }),
  },
  async ({ message }) => {
    return {
      content: [
        {
          type: "text",
          text: `Echo from dotnet-context-mcp: ${message}`,
        },
      ],
    };
  }
);

server.registerTool(
  "list_dbcontexts",
  {
    title: "List DbContexts",
    description:
      "Lists all EF Core DbContext classes found in a .NET solution. Uses Roslyn to analyze the solution and return DbContext class names, namespaces, project names, and file paths.",
    inputSchema: z.object({
      solutionPath: z
        .string()
        .describe("Absolute path to the .sln file to analyze"),
    }),
  },
  async ({ solutionPath }) => {
    const absolutePath = resolvePath(solutionPath);
    if (!existsSync(absolutePath)) {
      return {
        content: [{ type: "text", text: `Error: Solution file not found at ${absolutePath}` }],
        isError: true,
      };
    }
    return callCli("list-dbcontexts", [absolutePath], "list_dbcontexts");
  }
);

server.registerTool(
  "list_entities",
  {
    title: "List EF Core Entities",
    description:
      "Lists all EF Core entity classes (DbSet<T> properties) found in a .NET solution. Can optionally filter to a specific DbContext.",
    inputSchema: z.object({
      solutionPath: z
        .string()
        .describe("Absolute path to the .sln file to analyze"),
      dbContextName: z
        .string()
        .optional()
        .describe(
          "Optional: filter to a specific DbContext by name (e.g., 'TestDbContext')"
        ),
    }),
  },
  async ({ solutionPath, dbContextName }) => {
    const absolutePath = resolvePath(solutionPath);
    if (!existsSync(absolutePath)) {
      return {
        content: [{ type: "text", text: `Error: Solution file not found at ${absolutePath}` }],
        isError: true,
      };
    }
    const args = [absolutePath];
    if (dbContextName) args.push("--dbcontext", dbContextName);
    return callCli("list-entities", args, "list_entities");
  }
);

server.registerTool(
  "list_migrations",
  {
    title: "List EF Core Migrations",
    description:
      "Lists all EF Core migrations found in a .NET solution, organized by DbContext. Can filter to a specific DbContext. Includes migration ID, name, timestamp, and file path.",
    inputSchema: z.object({
      solutionPath: z
        .string()
        .describe("Absolute path to the .sln file to analyze"),
      dbContextName: z
        .string()
        .optional()
        .describe("Optional: filter to a specific DbContext by name"),
    }),
  },
  async ({ solutionPath, dbContextName }) => {
    const absolutePath = resolvePath(solutionPath);
    if (!existsSync(absolutePath)) {
      return {
        content: [{ type: "text", text: `Error: Solution file not found at ${absolutePath}` }],
        isError: true,
      };
    }
    const args = [absolutePath];
    if (dbContextName) args.push("--dbcontext", dbContextName);
    return callCli("list-migrations", args, "list_migrations");
  }
);

server.registerTool(
  "analyze_migration",
  {
    title: "Analyze EF Core Migration",
    description:
      "Returns the detailed Up/Down operations of a specific EF Core migration. Includes operation type (CreateTable, AddColumn, AlterColumn, CreateIndex, AddForeignKey, etc.), affected tables/columns, and a summary count by operation type. Use this after list_migrations to inspect what a particular migration actually does.",
    inputSchema: z.object({
      solutionPath: z
        .string()
        .describe("Absolute path to the .sln file to analyze"),
      migrationId: z
        .string()
        .describe(
          "The migration ID to analyze (e.g., '20260627141537_InitialCreate'). Get this from list_migrations output."
        ),
    }),
  },
  async ({ solutionPath, migrationId }) => {
    const absolutePath = resolvePath(solutionPath);
    if (!existsSync(absolutePath)) {
      return {
        content: [{ type: "text", text: `Error: Solution file not found at ${absolutePath}` }],
        isError: true,
      };
    }
    return callCli("analyze-migration", [absolutePath, migrationId], "analyze_migration");
  }
);

server.registerTool(
  "find_relationships",
  {
    title: "Find Entity Relationships",
    description:
      "Returns navigation properties, foreign keys, and relationship types (OneToMany, ManyToOne, OneToOne, ManyToMany) between entities in a specific DbContext. Detects conventions, data annotations, and fluent API configurations.",
    inputSchema: z.object({
      solutionPath: z.string().describe("Absolute path to the .sln file"),
      dbContextName: z.string().describe("The DbContext to analyze (e.g., 'TestDbContext')"),
      entity: z.string().optional().describe("Filter to a specific entity name"),
    }),
  },
  async ({ solutionPath, dbContextName, entity }) => {
    const absolutePath = resolvePath(solutionPath);
    if (!existsSync(absolutePath)) {
      return {
        content: [{ type: "text", text: `Error: Solution file not found at ${absolutePath}` }],
        isError: true,
      };
    }

    const args = [absolutePath, dbContextName];
    if (entity) args.push("--entity", entity);

    return callCli("list-relationships", args, "find_relationships");
  }
);

server.registerTool(
  "find_dbcontext_dependencies",
  {
    title: "Find DbContext DI Registrations",
    description:
      "Analyzes how DbContexts are registered in Dependency Injection across the solution. Returns registration method (AddDbContext, AddDbContextPool, AddDbContextFactory), provider (SqlServer, Npgsql, Sqlite, etc.), connection string source (Configuration, Hardcoded, EnvironmentVariable), lifetime, and location (file + line number). Use this to understand DbContext lifecycle and configuration across your app.",
    inputSchema: z.object({
      solutionPath: z.string().describe("Absolute path to the .sln file"),
    }),
  },
  async ({ solutionPath }) => {
    const absolutePath = resolvePath(solutionPath);
    if (!existsSync(absolutePath)) {
      return {
        content: [{ type: "text", text: `Error: Solution file not found at ${absolutePath}` }],
        isError: true,
      };
    }
    return callCli("find-dbcontext-dependencies", [absolutePath], "find_dbcontext_dependencies");
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);

console.error("dotnet-context-mcp server running on stdio");
