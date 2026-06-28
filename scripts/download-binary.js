#!/usr/bin/env node

/**
 * Post-install script: downloads the correct platform binary from GitHub Releases.
 *
 * Skipped if:
 * - User is in this repo (developer mode — cli/ source folder present)
 * - Binary already exists for current platform
 * - User opts out via DOTNET_CONTEXT_MCP_SKIP_DOWNLOAD=1
 */

import { existsSync, mkdirSync, createWriteStream, readFileSync } from "node:fs";
import { chmod, unlink } from "node:fs/promises";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { pipeline } from "node:stream/promises";
import { spawn } from "node:child_process";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const packageRoot = resolve(__dirname, "..");

// Skip if opted out
if (process.env.DOTNET_CONTEXT_MCP_SKIP_DOWNLOAD === "1") {
  console.log("[dotnet-context-mcp] Binary download skipped (DOTNET_CONTEXT_MCP_SKIP_DOWNLOAD=1)");
  process.exit(0);
}

// Skip if in development repo (cli/ source folder exists alongside package root)
const cliSourcePath = join(packageRoot, "cli", "DotnetContextMcp.Cli");
if (existsSync(cliSourcePath)) {
  console.log("[dotnet-context-mcp] Development mode detected — skipping binary download. Use 'npm run build:cli' to build locally.");
  process.exit(0);
}

// Detect platform
const platform = process.platform;
const arch = process.arch;

let rid;
if (platform === "win32" && arch === "x64")       rid = "win-x64";
else if (platform === "linux" && arch === "x64")   rid = "linux-x64";
else if (platform === "darwin" && arch === "x64")  rid = "osx-x64";
else if (platform === "darwin" && arch === "arm64") rid = "osx-arm64";
else {
  console.error(`[dotnet-context-mcp] Unsupported platform: ${platform}-${arch}`);
  console.error("Supported: win-x64, linux-x64, osx-x64, osx-arm64");
  console.error("The package installed but won't work on this platform without the binary.");
  process.exit(0);
}

const binaryDir = join(packageRoot, "build", "cli", rid);
const exeName = platform === "win32" ? "DotnetContextMcp.Cli.exe" : "DotnetContextMcp.Cli";
const binaryPath = join(binaryDir, exeName);

if (existsSync(binaryPath)) {
  console.log(`[dotnet-context-mcp] Binary already present at ${binaryPath}`);
  process.exit(0);
}

// Read version from package.json
const packageJson = JSON.parse(readFileSync(join(packageRoot, "package.json"), "utf-8"));
const version = packageJson.version;

// GitHub Releases URL
const archiveName = `dotnet-context-mcp-v${version}-${rid}.tar.gz`;
const downloadUrl = `https://github.com/sayinbrahim/dotnet-context-mcp/releases/download/v${version}/${archiveName}`;

console.log(`[dotnet-context-mcp] Downloading binary for ${rid}...`);
console.log(`  URL: ${downloadUrl}`);

mkdirSync(binaryDir, { recursive: true });
const tempArchive = join(packageRoot, "build", archiveName);

try {
  // Use Node's built-in fetch (Node 18+)
  const response = await fetch(downloadUrl);
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}: ${response.statusText}`);
  }

  const writeStream = createWriteStream(tempArchive);
  await pipeline(response.body, writeStream);

  console.log(`[dotnet-context-mcp] Downloaded ${archiveName}, extracting...`);

  // Extract using tar (available on all 4 platforms; Windows 10+ ships it)
  await new Promise((resolveExtract, rejectExtract) => {
    const tarProc = spawn("tar", ["-xzf", tempArchive, "-C", binaryDir], { stdio: "inherit" });
    tarProc.on("close", (code) => {
      if (code === 0) resolveExtract(undefined);
      else rejectExtract(new Error(`tar exited with code ${code}`));
    });
    tarProc.on("error", rejectExtract);
  });

  // Set executable permission on Unix
  if (platform !== "win32") {
    await chmod(binaryPath, 0o755);
  }

  // Clean up archive
  await unlink(tempArchive);

  console.log(`[dotnet-context-mcp] Installed binary at ${binaryPath}`);
} catch (error) {
  console.error(`[dotnet-context-mcp] Failed to download binary: ${error.message}`);
  console.error("");
  console.error("Possible causes:");
  console.error(`  - GitHub Release v${version} not yet published`);
  console.error("  - Network/firewall blocking GitHub");
  console.error("  - tar command not found on PATH");
  console.error("");
  console.error("You can:");
  console.error("  - Run 'npx dotnet-context-mcp' later (will retry download)");
  console.error("  - Download manually from https://github.com/sayinbrahim/dotnet-context-mcp/releases");
  // Don't fail the install — user can fix later
  process.exit(0);
}
