import * as fs from "fs";
import * as os from "os";
import * as path from "path";

export type McpClientType = "claude-code" | "cursor" | "continue-dev" | "windsurf";

export interface McpClientInfo {
  type: McpClientType;
  displayName: string;
  configPath: string;
  configScope: "user" | "project";
  configExists: boolean;
  configReadable: boolean;
  configValid: boolean;
  hasExistingMcpServers: boolean;
  detectionMethod: "config-file-exists" | "not-detected";
}

export interface DetectionResult {
  detectedClients: McpClientInfo[];
  notDetected: McpClientType[];
  currentWorkspaceHasNetSolution: boolean;
}

const ALL_CLIENT_TYPES: McpClientType[] = ["claude-code", "cursor", "continue-dev", "windsurf"];

export class ClientDetector {
  private readonly homeDir = os.homedir();

  async detect(workspaceFolder: string | undefined): Promise<DetectionResult> {
    const clients: McpClientInfo[] = [];

    for (const clientType of ALL_CLIENT_TYPES) {
      const info = await this.detectClient(clientType, workspaceFolder);
      if (info) {
        clients.push(info);
      }
    }

    let hasNetSolution = false;
    if (workspaceFolder) {
      const slnFiles = await this.findFiles(workspaceFolder, ".sln");
      hasNetSolution = slnFiles.length > 0;
    }

    return {
      detectedClients: clients,
      notDetected: this.getUndetectedTypes(clients),
      currentWorkspaceHasNetSolution: hasNetSolution,
    };
  }

  private async detectClient(
    type: McpClientType,
    workspaceFolder: string | undefined
  ): Promise<McpClientInfo | null> {
    const configPaths = this.getConfigPaths(type, workspaceFolder);

    for (const { path: configPath, scope } of configPaths) {
      const exists = await this.fileExists(configPath);
      if (exists) {
        const info = await this.probeConfigFile(configPath);
        return {
          type,
          displayName: this.getDisplayName(type),
          configPath,
          configScope: scope,
          configExists: true,
          configReadable: info.readable,
          configValid: info.valid,
          hasExistingMcpServers: info.hasMcpServers,
          detectionMethod: "config-file-exists",
        };
      }
    }

    return null;
  }

  private getConfigPaths(
    type: McpClientType,
    workspaceFolder: string | undefined
  ): Array<{ path: string; scope: "user" | "project" }> {
    const paths: Array<{ path: string; scope: "user" | "project" }> = [];

    switch (type) {
      case "claude-code":
        if (workspaceFolder) {
          paths.push({ path: path.join(workspaceFolder, ".mcp.json"), scope: "project" });
        }
        paths.push({ path: path.join(this.homeDir, ".claude.json"), scope: "user" });
        break;

      case "cursor":
        if (workspaceFolder) {
          paths.push({ path: path.join(workspaceFolder, ".cursor", "mcp.json"), scope: "project" });
        }
        paths.push({ path: path.join(this.homeDir, ".cursor", "mcp.json"), scope: "user" });
        break;

      case "continue-dev":
        paths.push({ path: path.join(this.homeDir, ".continue", "config.json"), scope: "user" });
        if (workspaceFolder) {
          paths.push({
            path: path.join(workspaceFolder, ".continue", "mcpServers", "mcp.json"),
            scope: "project",
          });
        }
        break;

      case "windsurf":
        paths.push({
          path: path.join(this.homeDir, ".codeium", "windsurf", "mcp_config.json"),
          scope: "user",
        });
        break;
    }

    return paths;
  }

  private getDisplayName(type: McpClientType): string {
    const names: Record<McpClientType, string> = {
      "claude-code": "Claude Code",
      "cursor": "Cursor",
      "continue-dev": "Continue.dev",
      "windsurf": "Windsurf",
    };
    return names[type];
  }

  private async fileExists(filePath: string): Promise<boolean> {
    try {
      await fs.promises.access(filePath, fs.constants.F_OK);
      return true;
    } catch {
      return false;
    }
  }

  private async probeConfigFile(
    filePath: string
  ): Promise<{ readable: boolean; valid: boolean; hasMcpServers: boolean }> {
    let content: string;
    try {
      content = await fs.promises.readFile(filePath, "utf-8");
    } catch {
      return { readable: false, valid: false, hasMcpServers: false };
    }

    try {
      const parsed = JSON.parse(content);
      const hasMcpServers =
        !!parsed.mcpServers ||
        !!(parsed.mcp && parsed.mcp.servers) ||
        false;

      return { readable: true, valid: true, hasMcpServers };
    } catch {
      return { readable: true, valid: false, hasMcpServers: false };
    }
  }

  private async findFiles(dir: string, extension: string): Promise<string[]> {
    try {
      const files = await fs.promises.readdir(dir);
      return files.filter((f) => f.endsWith(extension));
    } catch {
      return [];
    }
  }

  private getUndetectedTypes(detected: McpClientInfo[]): McpClientType[] {
    const detectedTypes = new Set(detected.map((c) => c.type));
    return ALL_CLIENT_TYPES.filter((t) => !detectedTypes.has(t));
  }
}
