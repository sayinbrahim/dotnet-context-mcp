import { createServer, IncomingMessage, ServerResponse } from "node:http";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WebStandardStreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/webStandardStreamableHttp.js";

function readRawBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolvePromise, rejectPromise) => {
    let raw = "";
    req.on("data", (chunk) => { raw += chunk; });
    req.on("end", () => resolvePromise(raw));
    req.on("error", rejectPromise);
  });
}

function toWebRequest(req: IncomingMessage, rawBody: string): Request {
  const headers = new Headers();
  for (const [key, value] of Object.entries(req.headers)) {
    if (value === undefined) continue;
    if (Array.isArray(value)) {
      for (const v of value) headers.append(key, v);
    } else {
      headers.set(key, value);
    }
  }
  const url = new URL(req.url ?? "/", `http://${req.headers.host ?? "localhost"}`);
  return new Request(url, {
    method: req.method,
    headers,
    body: rawBody ? rawBody : undefined,
  });
}

async function writeWebResponse(webResponse: Response, res: ServerResponse): Promise<void> {
  const headers: Record<string, string> = {};
  webResponse.headers.forEach((value, key) => { headers[key] = value; });
  res.writeHead(webResponse.status, headers);
  if (!webResponse.body) {
    res.end();
    return;
  }
  const reader = webResponse.body.getReader();
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    res.write(value);
  }
  res.end();
}

/**
 * Starts a stateless HTTP transport for the given MCP server, exposing a single
 * POST /mcp JSON-RPC endpoint. Stateless per the MCP 2026-07-28 spec: no session
 * ID is issued or tracked, so one WebStandardStreamableHTTPServerTransport instance
 * is shared across all requests.
 *
 * Uses the SDK's Web-standard transport (Request/Response) directly rather than the
 * Node-adapter (`StreamableHTTPServerTransport`, which wraps `@hono/node-server`) because
 * that adapter's `getRequestListener` silently swallows handler rejections into a bare,
 * unlogged 500 response — it calls `res.catch(handleFetchError)` with no errorHandler
 * wired through, so real failures (e.g. from `tools/list`/`tools/call` dispatch) vanish
 * with no trace anywhere. Talking to the Web-standard transport directly also matches
 * what a future Cloudflare Workers deployment would use.
 */
export async function startHttpTransport(server: McpServer, port: number): Promise<void> {
  const httpServer = createServer((req: IncomingMessage, res: ServerResponse) => {
    if (req.url !== "/mcp") {
      res.writeHead(404, { "Content-Type": "application/json" }).end(
        JSON.stringify({ error: "Not found. MCP endpoint is POST /mcp." })
      );
      return;
    }

    if (req.method !== "POST") {
      res.writeHead(405, { "Content-Type": "application/json" }).end(
        JSON.stringify({ error: "Method not allowed. Use POST." })
      );
      return;
    }

    // Stateless mode: a WebStandardStreamableHTTPServerTransport instance can only
    // handle a single request (the SDK throws on reuse), so a fresh transport is
    // connected to the shared `server` per request and closed once the response is
    // written. This serializes HTTP requests through the one McpServer instance
    // (Protocol.connect() rejects a second concurrent transport) — acceptable for
    // this local prototype, but a known limitation to revisit before real concurrent
    // load (e.g. a Server/transport pool, one per in-flight request).
    readRawBody(req)
      .then(async (rawBody) => {
        const transport = new WebStandardStreamableHTTPServerTransport({
          sessionIdGenerator: undefined,
          enableJsonResponse: true,
        });
        transport.onerror = (err) => {
          console.error("[http-transport] transport error:", err);
        };

        await server.connect(transport);
        try {
          const webRequest = toWebRequest(req, rawBody);
          const webResponse = await transport.handleRequest(webRequest);
          await writeWebResponse(webResponse, res);
        } finally {
          await server.close();
        }
      })
      .catch((err) => {
        console.error("[http-transport] Failed to handle request:", err);
        if (!res.headersSent) {
          res.writeHead(500, { "Content-Type": "application/json" }).end(
            JSON.stringify({ error: "Internal server error", detail: String(err) })
          );
        }
      });
  });

  await new Promise<void>((resolvePromise) => {
    httpServer.listen(port, () => resolvePromise());
  });

  console.error(`dotnet-context-mcp server running on http://localhost:${port}/mcp`);
}
