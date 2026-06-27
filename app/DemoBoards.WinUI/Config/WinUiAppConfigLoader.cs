using System;
using System.IO;
using System.Text.Json.Nodes;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Lib;

namespace DemoBoards_WinUI.Config;

public static class WinUiAppConfigLoader
{
    private const string ConfigFileName = "winui-app-config.json";

    public static WinUiAppConfig Load(string baseDirectory)
    {
        string repoRoot = RuntimeAssetResolver.ResolveWorkspaceRootOrThrow(baseDirectory, "WinUI app config loading");
        string appConfigPath = Path.Combine(baseDirectory, ConfigFileName);

        JsonObject root = File.Exists(appConfigPath)
            ? ParseJsonObject(File.ReadAllText(appConfigPath), ConfigFileName)
            : new JsonObject();

        string defaultNsCodeRoot = Path.Combine(repoRoot, "demo-boards-ns-code");
        string configuredRepoRoot = RuntimeAssetResolver.ResolvePathTemplate(
            root["repoRoot"]?.GetValue<string>(),
            repoRoot,
            baseDirectory,
            defaultNsCodeRoot) ?? repoRoot;
        JsonObject frontend = root["frontend"] as JsonObject ?? new JsonObject();
        JsonObject backend = root["backend"] as JsonObject ?? new JsonObject();
        JsonObject canvas = frontend["canvasLayout"] as JsonObject ?? new JsonObject();
        JsonObject boardServerConstants = frontend["boardServerConstants"] as JsonObject ?? new JsonObject();

        string? configuredNsCodeRoot = RuntimeAssetResolver.ResolvePathTemplate(
            backend["nsCodeRepoRoot"]?.GetValue<string>(),
            configuredRepoRoot,
            baseDirectory,
            defaultNsCodeRoot);
        string nsCodeRepoRoot = RuntimeAssetResolver.ResolveNsCodeRepoRootOrThrow(baseDirectory, configuredNsCodeRoot);

        BoardCanvasLayoutDefaults canvasLayout = new(
            ReadPositiveDouble(canvas, "defaultCardWidth", BoardCanvasLayoutDefaults.Default.DefaultCardWidth),
            ReadPositiveDouble(canvas, "defaultCardHeight", BoardCanvasLayoutDefaults.Default.DefaultCardHeight),
            ReadPositiveDouble(canvas, "columnGap", BoardCanvasLayoutDefaults.Default.ColumnGap),
            ReadPositiveDouble(canvas, "rowGap", BoardCanvasLayoutDefaults.Default.RowGap),
            ReadDouble(canvas, "originX", BoardCanvasLayoutDefaults.Default.OriginX),
            ReadDouble(canvas, "originY", BoardCanvasLayoutDefaults.Default.OriginY));

        WinUiBackendAppConfig backendDefaults = WinUiBackendAppConfig.CreateDefault(configuredRepoRoot, nsCodeRepoRoot, baseDirectory);
        WinUiBackendAppConfig backendConfig = new(
            nsCodeRepoRoot,
            RuntimeAssetResolver.ResolvePathTemplate(backend["hostInvocationRunnerPath"]?.GetValue<string>(), configuredRepoRoot, baseDirectory, nsCodeRepoRoot)
                ?? backendDefaults.HostInvocationRunnerPath,
            RuntimeAssetResolver.ResolvePathTemplate(backend["hostConfigPath"]?.GetValue<string>(), configuredRepoRoot, baseDirectory, nsCodeRepoRoot)
                ?? backendDefaults.HostConfigPath,
            RuntimeAssetResolver.ResolvePathTemplate(backend["templatesConfigPath"]?.GetValue<string>(), configuredRepoRoot, baseDirectory, nsCodeRepoRoot)
                ?? backendDefaults.TemplatesConfigPath,
            RuntimeAssetResolver.ResolvePathTemplate(backend["localFsConfigLoaderPath"]?.GetValue<string>(), configuredRepoRoot, baseDirectory, nsCodeRepoRoot)
                ?? backendDefaults.LocalFsConfigLoaderPath,
            RuntimeAssetResolver.ResolvePathTemplate(backend["hostedPrestartScriptPath"]?.GetValue<string>(), configuredRepoRoot, baseDirectory, nsCodeRepoRoot)
                ?? backendDefaults.HostedPrestartScriptPath,
            RuntimeAssetResolver.ResolvePathTemplate(backend["setupSingleAiWorkspaceScriptPath"]?.GetValue<string>(), configuredRepoRoot, baseDirectory, nsCodeRepoRoot)
                ?? backendDefaults.SetupSingleAiWorkspaceScriptPath,
            RuntimeAssetResolver.ResolvePathTemplate(backend["assistantRegistryPath"]?.GetValue<string>(), configuredRepoRoot, baseDirectory, nsCodeRepoRoot)
                ?? backendDefaults.AssistantRegistryPath);

        EnsureDirectoryExists(backendConfig.NsCodeRepoRoot, "backend.nsCodeRepoRoot");
        EnsureFileExists(backendConfig.HostInvocationRunnerPath, "backend.hostInvocationRunnerPath");

        WinUiBoardServerConstants serverConstants = new(
            ReadRequiredString(boardServerConstants, "agentOutputChannel", WinUiBoardServerConstants.Default.AgentOutputChannel),
            ReadRequiredString(boardServerConstants, "agentToolsChannel", WinUiBoardServerConstants.Default.AgentToolsChannel));

        return new WinUiAppConfig(
            appConfigPath,
            configuredRepoRoot,
            new WinUiFrontendAppConfig(canvasLayout, serverConstants),
            backendConfig);
    }

    private static void EnsureDirectoryExists(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Configured {label} does not exist: {path}");
        }
    }

    private static void EnsureFileExists(string path, string label)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configured {label} does not exist: {path}", path);
        }
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

    private static string ReadRequiredString(JsonObject source, string key, string fallback)
    {
        string? value = source[key]?.GetValue<string>()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}