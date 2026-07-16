import { McpClientType } from "./clientDetector.js";

export interface McpServerEntry {
  command: string;
  args: string[];
  env?: Record<string, string>;
}

export interface ConfigAdapter {
  read(rawConfig: any): Record<string, McpServerEntry>;
  write(rawConfig: any, servers: Record<string, McpServerEntry>): any;
}

// Claude Code, Cursor, Windsurf all use: root.mcpServers = { name: entry }
export class StandardMcpAdapter implements ConfigAdapter {
  read(rawConfig: any): Record<string, McpServerEntry> {
    return rawConfig?.mcpServers || {};
  }

  write(rawConfig: any, servers: Record<string, McpServerEntry>): any {
    return { ...rawConfig, mcpServers: servers };
  }
}

// Continue.dev uses experimental.modelContextProtocolServers as array
// OR mcpServers as object depending on version — start with object, fallback documented
export class ContinueDevAdapter implements ConfigAdapter {
  read(rawConfig: any): Record<string, McpServerEntry> {
    return rawConfig?.mcpServers || {};
  }

  write(rawConfig: any, servers: Record<string, McpServerEntry>): any {
    return { ...rawConfig, mcpServers: servers };
  }
}

export function getAdapter(clientType: McpClientType): ConfigAdapter {
  switch (clientType) {
    case "continue-dev":
      return new ContinueDevAdapter();
    default:
      return new StandardMcpAdapter(); // claude-code, cursor, windsurf
  }
}
