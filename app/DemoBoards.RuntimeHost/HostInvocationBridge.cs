using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;

namespace DemoBoards.RuntimeHost;

public sealed class HostInvocationBridge
{
    private readonly HostControlfaceBridge controlfaceBridge;
    private string serverUrl = string.Empty;
    private string mcpServerUrl = string.Empty;
    private readonly string runnerPath;
    private readonly string repoRoot;
    private string? lastInvocationJson;

    public HostInvocationBridge(HostControlfaceBridge controlfaceBridge, string serverUrl)
    {
        this.controlfaceBridge = controlfaceBridge ?? throw new ArgumentNullException(nameof(controlfaceBridge));
        runnerPath = Path.Combine(AppContext.BaseDirectory, "node", "host-invocation-runner.mjs");
        repoRoot = ResolveRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Unable to locate the ai-tool-evolver repo root from the WinUI runtime host.");
        UpdateServerUrl(serverUrl);
    }

    public void UpdateServerUrl(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new ArgumentException("Server URL is required.", nameof(serverUrl));
        }

        this.serverUrl = serverUrl.TrimEnd('/');
        mcpServerUrl = this.serverUrl + "/agent/mcp";
    }

    public string Invoke(string refJson, string argsJson)
    {
        lastInvocationJson = new JsonObject
        {
            ["ref"] = JsonNode.Parse(refJson),
            ["args"] = JsonNode.Parse(argsJson),
            ["provider"] = "copilot-foundry",
        }.ToJsonString();

        try
        {
            JsonObject invocationRef = ParseRequiredJsonObject(refJson, "Invocation ref must be a JSON object.");
            JsonObject args = ParseOptionalJsonObject(argsJson);
            JsonObject payload = BuildRunnerPayload(invocationRef, args);
            JsonObject result = ExecuteRunner(payload);
            return result.ToJsonString();
        }
        catch (Exception ex)
        {
            return new JsonObject
            {
                ["dispatched"] = false,
                ["error"] = ex.Message,
            }.ToJsonString();
        }
    }

    public string Describe(string refJson)
    {
        JsonObject invocationRef = ParseOptionalJsonObject(refJson);
        string kind = invocationRef["meta"]?.GetValue<string>()
            ?? invocationRef["howToRun"]?.GetValue<string>()
            ?? "chat-handler";

        return new JsonObject
        {
            ["name"] = "copilot-foundry-host",
            ["kind"] = kind,
            ["protocolVersion"] = "1.0",
            ["ref"] = invocationRef,
            ["supports"] = new JsonArray("invoke", "describe"),
        }.ToJsonString();
    }

    public string? GetLastInvocationJson()
    {
        return lastInvocationJson;
    }

    public void Reset()
    {
        lastInvocationJson = null;
    }

    private JsonObject BuildRunnerPayload(JsonObject invocationRef, JsonObject args)
    {
        string meta = invocationRef["meta"]?.GetValue<string>()?.Trim() ?? string.Empty;
        string boardId = ResolveBoardId(invocationRef, args);
        JsonObject boardRecord = GetBoardRecord(boardId);

        if (string.Equals(meta, "task-executor", StringComparison.Ordinal))
        {
            JsonObject request = args.DeepClone() as JsonObject ?? new JsonObject();
            JsonObject extra = request["extra"] as JsonObject ?? new JsonObject();
            foreach ((string key, JsonNode? value) in BuildTaskExecutorExtra(boardId, boardRecord))
            {
                extra[key] = value?.DeepClone();
            }

            request["extra"] = extra;
            return new JsonObject
            {
                ["mode"] = "task",
                ["repoRoot"] = repoRoot,
                ["boardId"] = boardId,
                ["request"] = request,
            };
        }

        if (string.Equals(meta, "chat-handler", StringComparison.Ordinal)
            || string.Equals(meta, "chat-handler-flow", StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["mode"] = "chat",
                ["repoRoot"] = repoRoot,
                ["boardId"] = boardId,
                ["serverUrl"] = serverUrl,
                ["mcpServerUrl"] = mcpServerUrl,
                ["agentFaceMcp"] = "/agent/mcp",
                ["boardRecord"] = boardRecord,
                ["request"] = new JsonObject
                {
                    ["ref"] = invocationRef.DeepClone(),
                    ["args"] = args.DeepClone(),
                },
            };
        }

        throw new InvalidOperationException($"Unsupported host invocation meta '{meta}'.");
    }

    private JsonObject BuildTaskExecutorExtra(string boardId, JsonObject boardRecord)
    {
        JsonObject extra = new()
        {
            ["boardId"] = boardId,
            ["serverUrl"] = serverUrl,
        };

        string aiWorkspaceRoot = boardRecord["aiWorkspaceRoot"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (aiWorkspaceRoot.Length > 0)
        {
            extra["aiWorkspaceRoot"] = aiWorkspaceRoot;
        }

        return extra;
    }

    private JsonObject ExecuteRunner(JsonObject payload)
    {
        if (!File.Exists(runnerPath))
        {
            throw new FileNotFoundException("The host invocation runner is missing from the runtime output.", runnerPath);
        }

        ProcessStartInfo startInfo = new("node")
        {
            WorkingDirectory = Path.Combine(repoRoot, "demo-boards-ns-code"),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(runnerPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch node for host invocation.");

        process.StandardInput.Write(payload.ToJsonString());
        process.StandardInput.Close();

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string errorText = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? $"Host invocation runner exited with code {process.ExitCode}."
                : errorText.Trim());
        }

        JsonNode? parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(stdout) ? "{}" : stdout);
        if (parsed is not JsonObject result)
        {
            throw new InvalidOperationException("Host invocation runner returned invalid JSON.");
        }

        if (result["dispatched"]?.GetValue<bool?>() == false)
        {
            string errorText = result["error"]?.GetValue<string>()?.Trim() ?? "Host invocation runner reported a dispatch failure.";
            throw new InvalidOperationException(errorText);
        }

        result["dispatched"] = true;
        return result;
    }

    private JsonObject GetBoardRecord(string boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            return new JsonObject();
        }

        string? raw = controlfaceBridge.GetBoardContainerRecordJson(boardId);
        return ParseOptionalJsonObject(raw);
    }

    private static string ResolveBoardId(JsonObject invocationRef, JsonObject args)
    {
        return FirstNonEmpty(
                args["boardId"]?.GetValue<string>(),
                args["board_id"]?.GetValue<string>(),
                invocationRef["extra"]?["boardId"]?.GetValue<string>(),
                invocationRef["extra"]?["board_id"]?.GetValue<string>())
            ?? "winui-board";
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
                bool hasYamlFlow = Directory.Exists(Path.Combine(current.FullName, "yaml-flow"));
                bool hasWinUi = Directory.Exists(Path.Combine(current.FullName, "demo-boards-winui-app"));
                if (hasDemoBoards && hasYamlFlow && hasWinUi)
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static JsonObject ParseRequiredJsonObject(string json, string errorMessage)
    {
        JsonNode? parsed = JsonNode.Parse(json);
        if (parsed is not JsonObject jsonObject)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return jsonObject;
    }

    private static JsonObject ParseOptionalJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        JsonNode? parsed = JsonNode.Parse(json);
        return parsed as JsonObject ?? new JsonObject();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}