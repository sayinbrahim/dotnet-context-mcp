# Releasing a new version

To create a new release:

1. Update version in:
   - `package.json`
   - `cli/DotnetContextMcp.Cli/DotnetContextMcp.Cli.csproj` (if version property exists)

2. Commit version bump:
   ```bash
   git commit -am "Release v0.1.0"
   ```

3. Create and push tag:
   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```

4. GitHub Action automatically:
   - Builds all 4 platform binaries (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`)
   - Creates a GitHub Release
   - Uploads tar.gz archives for each platform

5. After release succeeds:
   - `npm publish` (from Phase 13.2 setup)

## Naming convention

- Tags: `v<major>.<minor>.<patch>` (e.g., `v0.1.0`)
- Archives: `dotnet-context-mcp-v<version>-<rid>.tar.gz`

## Rollback

If a release is broken:

```bash
# Delete the GitHub Release manually via GitHub UI, then:
git push --delete origin v0.1.0

# If already published to npm (only within 72 hours):
npm unpublish dotnet-context-mcp@0.1.0
```
