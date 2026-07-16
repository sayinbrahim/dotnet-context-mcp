import { spawn } from "node:child_process";
import * as readline from "node:readline/promises";
import { stdin as input, stdout as output } from "node:process";
import { ClientDetector, McpClientInfo, DetectionResult } from "../clientConfig/clientDetector.js";
import { ConfigInjector, InjectionResult, isServerConfigured } from "../clientConfig/configInjector.js";

export interface InitClientOptions {
  client?: string[]; // From --client flag(s)
  remove?: boolean; // From --remove flag
  verify?: boolean; // From --verify flag
  yes?: boolean; // Skip confirmations
}

export async function runInitClient(options: InitClientOptions): Promise<number> {
  const detector = new ClientDetector();
  const injector = new ConfigInjector();

  // Get current working directory as workspace
  const workspaceFolder = process.cwd();
  const detection = await detector.detect(workspaceFolder);

  // === VERIFY MODE ===
  if (options.verify) {
    return runVerify(detection, injector);
  }

  // === REMOVE MODE ===
  if (options.remove) {
    return runRemove(detection, injector, options);
  }

  // === INSTALL MODE (default) ===
  return runInstall(detection, injector, options);
}

function resolveTargetClients(
  detection: DetectionResult,
  options: InitClientOptions
): McpClientInfo[] | null {
  if (options.client && options.client.length > 0) {
    const targetClients = detection.detectedClients.filter((c) =>
      options.client!.includes(c.type)
    );

    const notFound = options.client.filter(
      (t) => !detection.detectedClients.find((c) => c.type === t)
    );
    if (notFound.length > 0) {
      console.log(`⚠ Warning: These clients are not detected: ${notFound.join(", ")}`);
    }

    if (targetClients.length === 0) {
      console.log("None of the specified --client values match detected clients.");
      return null;
    }

    return targetClients;
  }

  return null;
}

async function runInstall(
  detection: DetectionResult,
  injector: ConfigInjector,
  options: InitClientOptions
): Promise<number> {
  console.log("dotnet-context-mcp — MCP Client Setup\n");

  if (detection.detectedClients.length === 0) {
    console.log("No MCP clients detected on this system.");
    console.log("");
    console.log("Please install one of:");
    console.log("  - Claude Code:  https://docs.claude.com/en/docs/claude-code");
    console.log("  - Cursor:       https://cursor.com");
    console.log("  - Continue.dev: https://continue.dev");
    console.log("  - Windsurf:     https://codeium.com/windsurf");
    return 1;
  }

  // Filter clients based on --client flag if provided
  let targetClients: McpClientInfo[];
  if (options.client && options.client.length > 0) {
    const resolved = resolveTargetClients(detection, options);
    if (resolved === null) {
      return 1;
    }
    targetClients = resolved;
  } else if (detection.detectedClients.length === 1) {
    // Only one, use it
    targetClients = detection.detectedClients;
  } else {
    // Interactive selection
    targetClients = await selectClientsInteractive(detection.detectedClients);
    if (targetClients.length === 0) {
      console.log("No clients selected. Aborting.");
      return 0;
    }
  }

  // Show what we're about to do
  console.log("\nWill install dotnet-context-mcp to:");
  for (const c of targetClients) {
    console.log(`  ✓ ${c.displayName}`);
    console.log(`    ${c.configPath}`);
  }
  console.log("");

  // Confirmation unless --yes
  if (!options.yes) {
    const confirmed = await confirm("Proceed?");
    if (!confirmed) {
      console.log("Aborted.");
      return 0;
    }
  }

  // Execute
  const results: InjectionResult[] = [];
  for (const client of targetClients) {
    const result = await injector.inject(client);
    results.push(result);

    if (result.success && !result.wasAlreadyInstalled) {
      console.log(`✓ Installed to ${client.displayName}`);
      if (result.backupPath) {
        console.log(`  Backup: ${result.backupPath}`);
      }
    } else if (result.wasAlreadyInstalled) {
      console.log(`— Already installed in ${client.displayName}, skipped`);
    } else {
      console.log(`✗ Failed for ${client.displayName}: ${result.error}`);
    }
  }

  // Summary
  const successCount = results.filter((r) => r.success && !r.wasAlreadyInstalled).length;
  const failCount = results.filter((r) => !r.success).length;

  console.log("");
  if (successCount > 0) {
    console.log(`Done. Restart your MCP client(s) to activate dotnet-context-mcp.`);
  }

  return failCount > 0 ? 1 : 0;
}

async function runRemove(
  detection: DetectionResult,
  injector: ConfigInjector,
  options: InitClientOptions
): Promise<number> {
  console.log("dotnet-context-mcp — Remove from MCP Client(s)\n");

  if (detection.detectedClients.length === 0) {
    console.log("No MCP clients detected on this system.");
    return 1;
  }

  let targetClients: McpClientInfo[];
  if (options.client && options.client.length > 0) {
    const resolved = resolveTargetClients(detection, options);
    if (resolved === null) {
      return 1;
    }
    targetClients = resolved;
  } else if (detection.detectedClients.length === 1) {
    targetClients = detection.detectedClients;
  } else {
    targetClients = await selectClientsInteractive(detection.detectedClients);
    if (targetClients.length === 0) {
      console.log("No clients selected. Aborting.");
      return 0;
    }
  }

  console.log("\nWill remove dotnet-context-mcp from:");
  for (const c of targetClients) {
    console.log(`  ✓ ${c.displayName}`);
    console.log(`    ${c.configPath}`);
  }
  console.log("");

  if (!options.yes) {
    const confirmed = await confirm("Proceed?");
    if (!confirmed) {
      console.log("Aborted.");
      return 0;
    }
  }

  const results: InjectionResult[] = [];
  for (const client of targetClients) {
    const result = await injector.remove(client);
    results.push(result);

    if (result.success && result.backupPath) {
      console.log(`✓ Removed from ${client.displayName}`);
      console.log(`  Backup: ${result.backupPath}`);
    } else if (result.success) {
      console.log(`— Not installed in ${client.displayName}, skipped`);
    } else {
      console.log(`✗ Failed for ${client.displayName}: ${result.error}`);
    }
  }

  const failCount = results.filter((r) => !r.success).length;

  console.log("");
  console.log("Done.");

  return failCount > 0 ? 1 : 0;
}

async function runVerify(detection: DetectionResult, injector: ConfigInjector): Promise<number> {
  console.log("dotnet-context-mcp — Verify Setup\n");

  if (detection.detectedClients.length === 0) {
    console.log("No MCP clients detected.");
    return 1;
  }

  // Check npx availability
  const npxAvailable = await checkNpx();
  console.log(`Runtime check: npx ${npxAvailable ? "available ✓" : "NOT AVAILABLE ✗"}`);

  if (!npxAvailable) {
    console.log("");
    console.log("npx is required. Install Node.js from https://nodejs.org");
    return 1;
  }

  // For each detected client, check if dotnet-context-mcp is configured
  console.log("");
  console.log("Client Status:");
  let anyMissing = false;
  for (const client of detection.detectedClients) {
    const configured = await isServerConfigured(client, "dotnet-context-mcp");
    if (!configured) {
      anyMissing = true;
    }
    console.log(`  ${configured ? "✓" : "✗"} ${client.displayName}: ${configured ? "configured" : "not configured"}`);
  }

  return anyMissing ? 1 : 0;
}

async function selectClientsInteractive(clients: McpClientInfo[]): Promise<McpClientInfo[]> {
  console.log("Multiple MCP clients detected. Select which to configure:");
  console.log("");
  for (let i = 0; i < clients.length; i++) {
    const c = clients[i];
    console.log(`  ${i + 1}. ${c.displayName} (${c.configPath})`);
  }
  console.log("  a. All");
  console.log("  q. Cancel");
  console.log("");

  const rl = readline.createInterface({ input, output });
  let answer: string;
  try {
    answer = await rl.question('Enter selection (comma-separated numbers, "a" for all, "q" to cancel): ');
  } finally {
    rl.close();
  }

  if (answer === "q" || answer === "") return [];
  if (answer === "a") return clients;

  const indices = answer
    .split(",")
    .map((s) => parseInt(s.trim(), 10) - 1)
    .filter((i) => !isNaN(i) && i >= 0 && i < clients.length);
  return indices.map((i) => clients[i]);
}

async function confirm(prompt: string): Promise<boolean> {
  const rl = readline.createInterface({ input, output });
  let answer: string;
  try {
    answer = await rl.question(`${prompt} [y/N]: `);
  } finally {
    rl.close();
  }
  return answer.toLowerCase() === "y" || answer.toLowerCase() === "yes";
}

async function checkNpx(): Promise<boolean> {
  return new Promise((resolve) => {
    // On Windows, npx resolves to npx.cmd, which requires a shell to execute.
    // Passing the whole invocation as a single string (rather than a command +
    // args array) avoids Node's shell-injection footgun since there are no
    // externally-controlled arguments here.
    const proc =
      process.platform === "win32"
        ? spawn("npx --version", { shell: true })
        : spawn("npx", ["--version"]);
    proc.on("exit", (code) => resolve(code === 0));
    proc.on("error", () => resolve(false));
  });
}
