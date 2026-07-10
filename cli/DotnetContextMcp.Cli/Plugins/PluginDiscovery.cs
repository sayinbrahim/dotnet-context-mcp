using System.Text.Json;

namespace DotnetContextMcp.Cli.Plugins;

public record DotnetContextMcpConfig(
    string[]? Plugins
);

public class PluginDiscovery
{
    public List<string> FindPluginFiles(string solutionDirectory)
    {
        var files = new List<string>();

        var conventionDir = Path.Combine(solutionDirectory, ".dotnet-context-mcp", "plugins");
        if (Directory.Exists(conventionDir))
        {
            files.AddRange(Directory.GetFiles(conventionDir, "*.json", SearchOption.TopDirectoryOnly));
        }

        var configFile = Path.Combine(solutionDirectory, "dotnet-context-mcp.config.json");
        if (File.Exists(configFile))
        {
            try
            {
                var configContent = File.ReadAllText(configFile);
                var config = JsonSerializer.Deserialize<DotnetContextMcpConfig>(configContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config?.Plugins != null)
                {
                    foreach (var pluginPath in config.Plugins)
                    {
                        var resolved = Path.IsPathRooted(pluginPath)
                            ? pluginPath
                            : Path.Combine(solutionDirectory, pluginPath);

                        if (File.Exists(resolved))
                        {
                            files.Add(Path.GetFullPath(resolved));
                        }
                    }
                }
            }
            catch
            {
                // Config file exists but is malformed; PluginLoader surfaces discovery issues via errors list.
            }
        }

        return files
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
