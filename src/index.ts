import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

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

const transport = new StdioServerTransport();
await server.connect(transport);

console.error("dotnet-context-mcp server running on stdio");
