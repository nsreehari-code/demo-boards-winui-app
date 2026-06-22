using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DemoBoards_WinUI.Config;

public sealed record WinUiHostTemplateCatalog(
    IReadOnlyList<string> AssistantNames,
    IReadOnlyList<string> AiWorkspaceTemplateNames,
    IReadOnlyList<string> UiTemplateNames,
    IReadOnlyList<string> RefsTemplateNames,
    string HostConfigPath,
    string TemplatesConfigPath,
    string SetupSingleAiWorkspaceScriptPath,
    string SampleTemplateCatalogDir,
    string RuntimeBoardsIndexRef,
    string RuntimeBoardsLayoutRef,
    string RawHostSummaryJson);

public sealed class WinUiHostConfigService
{
    private readonly WinUiAppConfig appConfig;
    private readonly string runnerPath;

    public WinUiHostConfigService(WinUiAppConfig appConfig, string baseDirectory)
    {
        this.appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        runnerPath = Path.Combine(baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)), "node", "winui-host-config-runner.mjs");
    }

    public async Task<WinUiHostTemplateCatalog> LoadTemplateCatalogAsync()
    {
        JsonObject payload = new()
        {
            ["mode"] = "describe-host-config",
            ["hostConfigPath"] = appConfig.Backend.HostConfigPath,
            ["localFsConfigLoaderPath"] = appConfig.Backend.LocalFsConfigLoaderPath,
            ["templatesConfigPath"] = appConfig.Backend.TemplatesConfigPath,
            ["assistantRegistryPath"] = appConfig.Backend.AssistantRegistryPath,
            ["setupSingleAiWorkspaceScriptPath"] = appConfig.Backend.SetupSingleAiWorkspaceScriptPath,
        };
        JsonObject result = await InvokeRunnerAsync(payload).ConfigureAwait(false);

        return new WinUiHostTemplateCatalog(
            ReadStringList(result["assistantNames"]),
            ReadStringList(result["aiWorkspaceTemplateNames"]),
            ReadStringList(result["uiTemplateNames"]),
            ReadStringList(result["refsTemplateNames"]),
            result["hostConfigPath"]?.GetValue<string>() ?? appConfig.Backend.HostConfigPath,
            result["templatesConfigPath"]?.GetValue<string>() ?? appConfig.Backend.TemplatesConfigPath,
            result["setupSingleAiWorkspaceScriptPath"]?.GetValue<string>() ?? appConfig.Backend.SetupSingleAiWorkspaceScriptPath,
            result["sampleTemplateCatalogDir"]?.GetValue<string>() ?? string.Empty,
            result["runtimeBoardsIndexRef"]?.GetValue<string>() ?? string.Empty,
            result["runtimeBoardsLayoutRef"]?.GetValue<string>() ?? string.Empty,
            result["rawHostSummaryJson"]?.GetValue<string>() ?? "{}");
    }

    public async Task<string> ResolveBoardConfigJsonAsync(string boardId, string recordJson)
    {
        JsonNode? record = JsonNode.Parse(string.IsNullOrWhiteSpace(recordJson) ? "{}" : recordJson);
        JsonObject payload = new()
        {
            ["mode"] = "resolve-board-config",
            ["boardId"] = boardId,
            ["record"] = record,
            ["hostConfigPath"] = appConfig.Backend.HostConfigPath,
            ["localFsConfigLoaderPath"] = appConfig.Backend.LocalFsConfigLoaderPath,
        };

        JsonObject result = await InvokeRunnerAsync(payload).ConfigureAwait(false);
        JsonNode? resolved = result["resolvedBoardConfig"];
        return resolved?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
    }

    public async Task SyncBoardRecordAsync(string boardId, string recordJson)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            throw new InvalidOperationException("Board id is required for host config sync.");
        }

        JsonNode? record = JsonNode.Parse(string.IsNullOrWhiteSpace(recordJson) ? "{}" : recordJson);
        JsonObject payload = new()
        {
            ["mode"] = "sync-board-record",
            ["boardId"] = boardId,
            ["record"] = record,
            ["hostConfigPath"] = appConfig.Backend.HostConfigPath,
            ["localFsConfigLoaderPath"] = appConfig.Backend.LocalFsConfigLoaderPath,
        };
        _ = await InvokeRunnerAsync(payload).ConfigureAwait(false);
    }

    public async Task SetupBoardWorkspaceAsync(string boardId, string recordJson)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            throw new InvalidOperationException("Board id is required for workspace setup.");
        }

        await SyncBoardRecordAsync(boardId, recordJson).ConfigureAwait(false);

        ProcessStartInfo startInfo = new("node")
        {
            WorkingDirectory = Path.GetDirectoryName(appConfig.Backend.SetupSingleAiWorkspaceScriptPath) ?? appConfig.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(appConfig.Backend.SetupSingleAiWorkspaceScriptPath);
        startInfo.ArgumentList.Add(boardId.Trim());
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(appConfig.Backend.HostConfigPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch the board workspace setup helper.");
        string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            string errorText = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? $"Workspace setup failed for board '{boardId}'."
                : errorText.Trim());
        }
    }

    private async Task<JsonObject> InvokeRunnerAsync(JsonObject payload)
    {
        if (!File.Exists(runnerPath))
        {
            throw new FileNotFoundException("The WinUI host-config runner is missing from the app output.", runnerPath);
        }

        ProcessStartInfo startInfo = new("node")
        {
            WorkingDirectory = appConfig.RepoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(runnerPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch the WinUI host-config runner.");
        await process.StandardInput.WriteAsync(payload.ToJsonString()).ConfigureAwait(false);
        process.StandardInput.Close();

        string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            string errorText = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? "The WinUI host-config runner failed."
                : errorText.Trim());
        }

        JsonNode? parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(stdout) ? "{}" : stdout);
        if (parsed is not JsonObject jsonObject)
        {
            throw new InvalidOperationException("The WinUI host-config runner returned invalid JSON.");
        }

        if (jsonObject["ok"]?.GetValue<bool?>() == false)
        {
            throw new InvalidOperationException(jsonObject["error"]?.GetValue<string>() ?? "The WinUI host-config runner reported a failure.");
        }

        return jsonObject;
    }

    private static IReadOnlyList<string> ReadStringList(JsonNode? node)
    {
        return node is JsonArray array
            ? array.Select(item => item?.GetValue<string>()?.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray()
            : Array.Empty<string>();
    }
}