import * as fs from "fs";
import * as path from "path";
import { McpClientInfo } from "./clientDetector.js";
import { getAdapter } from "./configSchema.js";

export interface InjectionOptions {
  serverName?: string;
  command?: string;
  args?: string[];
}

export interface InjectionResult {
  success: boolean;
  client: McpClientInfo;
  backupPath?: string;
  error?: string;
  wasAlreadyInstalled?: boolean;
}

export async function isServerConfigured(
  client: McpClientInfo,
  serverName: string = "dotnet-context-mcp"
): Promise<boolean> {
  if (!client.configExists || !client.configValid) {
    return false;
  }
  try {
    const content = await fs.promises.readFile(client.configPath, "utf-8");
    const rawConfig = JSON.parse(content);
    const adapter = getAdapter(client.type);
    const servers = adapter.read(rawConfig);
    return !!servers[serverName];
  } catch {
    return false;
  }
}

export class ConfigInjector {
  async inject(
    client: McpClientInfo,
    options: InjectionOptions = {}
  ): Promise<InjectionResult> {
    const serverName = options.serverName || "dotnet-context-mcp";
    const command = options.command || "npx";
    const args = options.args || ["-y", "dotnet-context-mcp@latest"];

    try {
      // Step 1: Ensure directory exists
      await this.ensureDirectory(path.dirname(client.configPath));

      // Step 2: Read existing config (or start empty)
      let rawConfig: any = {};
      if (client.configExists && client.configValid) {
        const content = await fs.promises.readFile(client.configPath, "utf-8");
        rawConfig = JSON.parse(content);
      } else if (client.configExists && !client.configValid) {
        return {
          success: false,
          client,
          error: "Existing config is invalid JSON. Please fix manually before proceeding.",
        };
      }

      // Step 3: Get adapter and current servers
      const adapter = getAdapter(client.type);
      const currentServers = adapter.read(rawConfig);

      // Check if already installed
      if (currentServers[serverName]) {
        return {
          success: true,
          client,
          wasAlreadyInstalled: true,
        };
      }

      // Step 4: Backup (only if file exists)
      let backupPath: string | undefined;
      if (client.configExists) {
        backupPath = await this.createBackup(client.configPath);
      }

      // Step 5: Merge new server entry
      const newServers = {
        ...currentServers,
        [serverName]: { command, args },
      };

      // Step 6: Write updated config
      const newConfig = adapter.write(rawConfig, newServers);
      await fs.promises.writeFile(
        client.configPath,
        JSON.stringify(newConfig, null, 2),
        "utf-8"
      );

      return { success: true, client, backupPath };
    } catch (err) {
      return {
        success: false,
        client,
        error: err instanceof Error ? err.message : String(err),
      };
    }
  }

  async remove(client: McpClientInfo, serverName: string = "dotnet-context-mcp"): Promise<InjectionResult> {
    try {
      if (!client.configExists || !client.configValid) {
        return { success: true, client }; // Nothing to remove
      }

      const content = await fs.promises.readFile(client.configPath, "utf-8");
      const rawConfig = JSON.parse(content);
      const adapter = getAdapter(client.type);
      const currentServers = adapter.read(rawConfig);

      if (!currentServers[serverName]) {
        return { success: true, client }; // Not installed
      }

      // Backup
      const backupPath = await this.createBackup(client.configPath);

      // Remove
      const { [serverName]: _removed, ...remainingServers } = currentServers;
      const newConfig = adapter.write(rawConfig, remainingServers);

      await fs.promises.writeFile(
        client.configPath,
        JSON.stringify(newConfig, null, 2),
        "utf-8"
      );

      return { success: true, client, backupPath };
    } catch (err) {
      return {
        success: false,
        client,
        error: err instanceof Error ? err.message : String(err),
      };
    }
  }

  async rollback(backupPath: string): Promise<boolean> {
    try {
      const originalPath = backupPath.replace(/\.backup-\d+$/, "");
      const backupContent = await fs.promises.readFile(backupPath, "utf-8");
      await fs.promises.writeFile(originalPath, backupContent, "utf-8");
      return true;
    } catch {
      return false;
    }
  }

  private async ensureDirectory(dirPath: string): Promise<void> {
    await fs.promises.mkdir(dirPath, { recursive: true });
  }

  private async createBackup(configPath: string): Promise<string> {
    const timestamp = Date.now();
    const backupPath = `${configPath}.backup-${timestamp}`;
    await fs.promises.copyFile(configPath, backupPath);
    return backupPath;
  }
}
