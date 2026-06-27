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
        content: [
          {
            type: "text",
            text: `Error: Solution file not found at ${absolutePath}`,
          },
        ],
        isError: true,
      };
    }

    // build/index.js lives one level inside the repo root
    const projectRoot = resolvePath(__dirname, "..");
    const cliProjectPath = resolvePath(projectRoot, "cli/DotnetContextMcp.Cli");

    const result = await new Promise<{
      stdout: string;
      stderr: string;
      exitCode: number;
    }>((resolveResult, rejectResult) => {
      const proc = spawn(
        "dotnet",
        ["run", "--project", cliProjectPath, "--", "list-dbcontexts", absolutePath],
        { cwd: projectRoot }
      );

      let stdout = "";
      let stderr = "";

      proc.stdout.on("data", (chunk) => {
        stdout += chunk.toString();
      });
      proc.stderr.on("data", (chunk) => {
        stderr += chunk.toString();
      });

      proc.on("close", (code) => {
        resolveResult({ stdout, stderr, exitCode: code ?? -1 });
      });

      proc.on("error", (err) => {
        rejectResult(err);
      });
    });

    if (result.stderr) {
      console.error("[list_dbcontexts] CLI stderr:", result.stderr);
    }

    if (result.exitCode !== 0) {
      return {
        content: [
          {
            type: "text",
            text: `Error: .NET CLI exited with code ${result.exitCode}\n\nStderr:\n${result.stderr}`,
          },
        ],
        isError: true,
      };
    }

    try {
      const data = JSON.parse(result.stdout);
      return {
        content: [
          {
            type: "text",
            text: JSON.stringify(data, null, 2),
          },
        ],
      };
    } catch {
      return {
        content: [
          {
            type: "text",
            text: `Error parsing CLI output as JSON:\n\nStdout:\n${result.stdout}\n\nStderr:\n${result.stderr}`,
          },
        ],
        isError: true,
      };
    }
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);

console.error("dotnet-context-mcp server running on stdio");
