using System;
using System.IO;
using System.Text.Json.Nodes;

namespace DemoBoards_WinUI.Config;

public static class WinUiAppConfigLoader
{
    private const string ConfigFileName = "winui-app-config.json";

    public static WinUiAppConfig Load(string baseDirectory)
    {
        string repoRoot = ResolveRepoRoot(baseDirectory)
            ?? throw new InvalidOperationException("Unable to locate the ai-tool-evolver repo root for WinUI app config loading.");
        string appConfigPath = Path.Combine(baseDirectory, ConfigFileName);

        JsonObject root = File.Exists(appConfigPath)
            ? ParseJsonObject(File.ReadAllText(appConfigPath), ConfigFileName)
            : new JsonObject();

        string configuredRepoRoot = ResolvePath(root["repoRoot"]?.GetValue<string>(), repoRoot) ?? repoRoot;
        JsonObject frontend = root["frontend"] as JsonObject ?? new JsonObject();
        JsonObject backend = root["backend"] as JsonObject ?? new JsonObject();
        JsonObject canvas = frontend["canvasLayout"] as JsonObject ?? new JsonObject();

        BoardCanvasLayoutDefaults canvasLayout = new(
            ReadPositiveDouble(canvas, "defaultCardWidth", BoardCanvasLayoutDefaults.Default.DefaultCardWidth),
            ReadPositiveDouble(canvas, "defaultCardHeight", BoardCanvasLayoutDefaults.Default.DefaultCardHeight),
            ReadPositiveDouble(canvas, "columnGap", BoardCanvasLayoutDefaults.Default.ColumnGap),
            ReadPositiveDouble(canvas, "rowGap", BoardCanvasLayoutDefaults.Default.RowGap),
            ReadDouble(canvas, "originX", BoardCanvasLayoutDefaults.Default.OriginX),
            ReadDouble(canvas, "originY", BoardCanvasLayoutDefaults.Default.OriginY));

        WinUiBackendAppConfig backendDefaults = WinUiBackendAppConfig.CreateDefault(configuredRepoRoot);
        WinUiBackendAppConfig backendConfig = new(
            ResolvePath(backend["hostConfigPath"]?.GetValue<string>(), configuredRepoRoot) ?? backendDefaults.HostConfigPath,
            ResolvePath(backend["templatesConfigPath"]?.GetValue<string>(), configuredRepoRoot) ?? backendDefaults.TemplatesConfigPath,
            ResolvePath(backend["localFsConfigLoaderPath"]?.GetValue<string>(), configuredRepoRoot) ?? backendDefaults.LocalFsConfigLoaderPath,
            ResolvePath(backend["hostedPrestartScriptPath"]?.GetValue<string>(), configuredRepoRoot) ?? backendDefaults.HostedPrestartScriptPath,
            ResolvePath(backend["setupSingleAiWorkspaceScriptPath"]?.GetValue<string>(), configuredRepoRoot) ?? backendDefaults.SetupSingleAiWorkspaceScriptPath,
            ResolvePath(backend["assistantRegistryPath"]?.GetValue<string>(), configuredRepoRoot) ?? backendDefaults.AssistantRegistryPath);

        return new WinUiAppConfig(
            appConfigPath,
            configuredRepoRoot,
            new WinUiFrontendAppConfig(canvasLayout),
            backendConfig);
    }

    private static string? ResolveRepoRoot(string startPath)
    {
        foreach (string seed in new[] { startPath, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? current = Directory.Exists(seed)
                ? new DirectoryInfo(seed)
                : Path.GetDirectoryName(seed) is string parentPath
                    ? new DirectoryInfo(parentPath)
                    : null;
            while (current is not null)
            {
                bool hasDemoBoards = Directory.Exists(Path.Combine(current.FullName, "demo-boards-ns-code"));
                bool hasFrontend = Directory.Exists(Path.Combine(current.FullName, "demo-boards-frontend"));
                bool hasWinUi = Directory.Exists(Path.Combine(current.FullName, "demo-boards-winui-app"));
                if (hasDemoBoards && hasFrontend && hasWinUi)
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static string? ResolvePath(string? rawPath, string repoRoot)
    {
        string normalized = rawPath?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return null;
        }

        string expanded = normalized.Replace("${repoRoot}", repoRoot, StringComparison.OrdinalIgnoreCase);
        return Path.GetFullPath(expanded);
    }

    private static JsonObject ParseJsonObject(string rawJson, string label)
    {
        JsonNode? parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson);
        if (parsed is not JsonObject jsonObject)
        {
            throw new InvalidOperationException($"{label} must be a JSON object.");
        }

        return jsonObject;
    }

    private static double ReadPositiveDouble(JsonObject source, string key, double fallback)
    {
        double value = ReadDouble(source, key, fallback);
        return value > 0 ? value : fallback;
    }

    private static double ReadDouble(JsonObject source, string key, double fallback)
    {
        return source[key]?.GetValue<double?>() is double value && double.IsFinite(value)
            ? value
            : fallback;
    }
}