# syntax=docker/dockerfile:1

# ---- builder: install deps and compile TypeScript ----
FROM node:22-slim AS builder
WORKDIR /app

COPY package.json package-lock.json* ./
COPY scripts ./scripts
# The .NET CLI binary is fetched by postinstall from GitHub Releases; skip that
# here and copy the already-published linux-x64 binary from the build context
# instead, since the release for this version may not exist yet.
ENV DOTNET_CONTEXT_MCP_SKIP_DOWNLOAD=1
RUN npm ci

COPY tsconfig.json ./
COPY src ./src
RUN npm run build

# ---- runtime: minimal image with Node + the .NET self-contained binary ----
FROM node:22-slim AS runtime
WORKDIR /app

# The published CLI is a self-contained, glibc-linked net8.0 app (needs ICU +
# OpenSSL + ca-certificates). It will NOT run on musl-based images (Alpine).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libicu72 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV NODE_ENV=production
ENV DOTNET_CONTEXT_MCP_SKIP_DOWNLOAD=1

COPY package.json package-lock.json* ./
COPY scripts ./scripts
RUN npm ci --omit=dev

COPY bin ./bin
COPY --from=builder /app/build ./build
COPY build/cli/linux-x64 ./build/cli/linux-x64

RUN npm link

EXPOSE 3000
ENV PORT=3000

CMD ["sh", "-c", "exec dotnet-context-mcp --transport http --port ${PORT}"]
