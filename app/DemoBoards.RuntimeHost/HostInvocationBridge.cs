using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DemoBoards.RuntimeHost;

public sealed class HostInvocationBridge
{
    private readonly HostControlfaceBridge controlfaceBridge;
    private string serverUrl = string.Empty;
    private string mcpServerUrl = string.Empty;
    private readonly string runnerPath;
    private readonly string workspaceRoot;
    private readonly string nsCodeRepoRoot;
    private string? lastInvocationJson;

    public HostInvocationBridge(HostControlfaceBridge controlfaceBridge, string serverUrl)
    {
        this.controlfaceBridge = controlfaceBridge ?? throw new ArgumentNullException(nameof(controlfaceBridge));
        runnerPath = RuntimeAssetResolver.ResolveHostInvocationRunnerPathOrThrow(AppContext.BaseDirectory);
        workspaceRoot = RuntimeAssetResolver.ResolveWorkspaceRootOrThrow(AppContext.BaseDirectory, "WinUI runtime host");
        nsCodeRepoRoot = RuntimeAssetResolver.ResolveNsCodeRepoRootOrThrow(AppContext.BaseDirectory);
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

    public string GetServerUrl()
    {
        return serverUrl;
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

    /// <summary>
    /// Fire-and-forget task-executor dispatch used by the non-core platform adapter's
    /// `dispatchExecution`. The board-worker runs out-of-process and reports completion
    /// back through the HTTP `/mcp-webhooks` callback, which must be served by the same
    /// (single-threaded) embedded runtime. Running the worker synchronously would block
    /// the runtime thread and deadlock against that callback, so the runner is launched
    /// on a background thread and this method returns `{ dispatched: true }` immediately.
    /// </summary>
    public string InvokeDispatch(string refJson, string argsJson)
    {
        try
        {
            JsonObject invocationRef = ParseRequiredJsonObject(refJson, "Invocation ref must be a JSON object.");
            JsonObject args = ParseOptionalJsonObject(argsJson);
            JsonObject payload = BuildRunnerPayload(invocationRef, args);
            StartRunnerDetached(payload);
            return new JsonObject { ["dispatched"] = true }.ToJsonString();
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

    private void StartRunnerDetached(JsonObject payload)
    {
        if (!File.Exists(runnerPath))
        {
            throw new FileNotFoundException("The host invocation runner is missing from the runtime output.", runnerPath);
        }

        string payloadJson = payload.ToJsonString();

        _ = Task.Run(() =>
        {
            try
            {
                ProcessStartInfo startInfo = new("node")
                {
                    WorkingDirectory = nsCodeRepoRoot,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add(runnerPath);

                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to launch node for host invocation dispatch.");

                process.StandardInput.Write(payloadJson);
                process.StandardInput.Close();

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                WriteDispatchDiagnostic(
                    $"mode={payload["mode"]?.GetValue<string>() ?? "<none>"} exit={process.ExitCode}\nstdout: {stdout?.Trim()}\nstderr: {stderr?.Trim()}");

                if (process.ExitCode != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    Console.Error.WriteLine($"[host-invocation-dispatch] runner exited {process.ExitCode}: {detail?.Trim()}");
                }
            }
            catch (Exception ex)
            {
                WriteDispatchDiagnostic($"background runner failed: {ex}");
                Console.Error.WriteLine($"[host-invocation-dispatch] background runner failed: {ex.Message}");
            }
        });
    }

    private void WriteDispatchDiagnostic(string message)
    {
        try
        {
            string logPath = Path.Combine(Path.GetTempPath(), "winui-host-dispatch.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n\n");
        }
        catch
        {
            // Diagnostics are best-effort only.
        }
    }

    public string Describe(string refJson)
    {
        JsonObject invocationRef = ParseOptionalJsonObject(refJson);        string kind = invocationRef["meta"]?.GetValue<string>()
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

    /// <summary>
    /// Request/response executor invocation used by the non-core platform adapter
    /// (validate-source-def, describe-capabilities, run-source-preflight, probe-source-preflight, …).
    /// Mirrors the yaml-flow non-core dispatcher's `local-node` branch: spawns
    /// `node task-executor.js &lt;subcommand&gt; --extra &lt;base64&gt;` with the input on stdin
    /// and returns the executor's raw stdout. The hosted/embedded task-executor ref is
    /// resolved to the immediate local task-executor script (the host-side `resolveRef` rewrite).
    /// </summary>
    public string InvokeExecutor(string refJson, string subcommand, string? inputText)
    {
        JsonObject invocationRef = ParseRequiredJsonObject(refJson, "Executor ref must be a JSON object.");
        string normalizedSubcommand = subcommand?.Trim() ?? string.Empty;
        if (normalizedSubcommand.Length == 0)
        {
            throw new InvalidOperationException("Executor subcommand is required.");
        }

        string boardId = ResolveBoardId(invocationRef, new JsonObject());
        JsonObject boardRecord = GetBoardRecord(boardId);

        JsonObject extra = (invocationRef["extra"]?.DeepClone() as JsonObject) ?? new JsonObject();
        foreach ((string key, JsonNode? value) in BuildTaskExecutorExtra(boardId, boardRecord))
        {
            extra[key] = value?.DeepClone();
        }

        string taskExecutorPath = Path.Combine(nsCodeRepoRoot, "demo-board", "server", "board-worker", "task-executor.js");
        if (!File.Exists(taskExecutorPath))
        {
            throw new FileNotFoundException("The board-worker task executor script is missing.", taskExecutorPath);
        }

        return RunLocalNodeExecutor(taskExecutorPath, normalizedSubcommand, extra, inputText);
    }

    private string RunLocalNodeExecutor(string scriptPath, string subcommand, JsonObject extra, string? inputText)
    {
        string extraBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(extra.ToJsonString()));

        ProcessStartInfo startInfo = new("node")
        {
            WorkingDirectory = nsCodeRepoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(subcommand);
        startInfo.ArgumentList.Add("--extra");
        startInfo.ArgumentList.Add(extraBase64);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch node for executor invocation.");

        if (!string.IsNullOrEmpty(inputText))
        {
            process.StandardInput.Write(inputText);
        }

        process.StandardInput.Close();

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string errorText = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? $"Executor subcommand '{subcommand}' exited with code {process.ExitCode}."
                : errorText.Trim());
        }

        return stdout;
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
        ValidateInvocationRef(meta, invocationRef);
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
                ["repoRoot"] = workspaceRoot,
                ["nsCodeRepoRoot"] = nsCodeRepoRoot,
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
                ["repoRoot"] = workspaceRoot,
                ["nsCodeRepoRoot"] = nsCodeRepoRoot,
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
            ["mcpServerUrl"] = mcpServerUrl,
            ["agentFaceMcp"] = "/agent/mcp",
            ["apiBasePath"] = $"/api/boards/{Uri.EscapeDataString(boardId)}",
        };

        string aiWorkspaceRoot = boardRecord["aiWorkspaceRoot"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (aiWorkspaceRoot.Length > 0)
        {
            extra["aiWorkspaceRoot"] = aiWorkspaceRoot;
        }

        string foundryEndpoint = boardRecord["foundryEndpoint"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (foundryEndpoint.Length > 0)
        {
            extra["foundryEndpoint"] = foundryEndpoint;
        }

        string foundryTaskExecutorAgentId = boardRecord["foundryTaskExecutorAgentId"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (foundryTaskExecutorAgentId.Length > 0)
        {
            extra["foundryTaskExecutorAgentId"] = foundryTaskExecutorAgentId;
        }

        return extra;
    }

    private static void ValidateInvocationRef(string meta, JsonObject invocationRef)
    {
        string howToRun = invocationRef["howToRun"]?.GetValue<string>()?.Trim() ?? string.Empty;

        if (string.Equals(meta, "task-executor", StringComparison.Ordinal))
        {
            if (IsAllowedTaskExecutorHowToRun(howToRun))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Unsupported task-executor howToRun '{howToRun}'. Allowed: embedded-host, queue-storage, local-node, http:post, http:get, in-process-loop.");
        }

        if (string.Equals(meta, "chat-handler", StringComparison.Ordinal)
            || string.Equals(meta, "chat-handler-flow", StringComparison.Ordinal))
        {
            if (IsAllowedChatHowToRun(howToRun))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Unsupported chat howToRun '{howToRun}'. Allowed: embedded-host, built-in, host-llm, local-node.");
        }
    }

    private static bool IsAllowedTaskExecutorHowToRun(string howToRun)
    {
        return string.Equals(howToRun, "embedded-host", StringComparison.Ordinal)
            || string.Equals(howToRun, "queue-storage", StringComparison.Ordinal)
            || string.Equals(howToRun, "local-node", StringComparison.Ordinal)
            || string.Equals(howToRun, "http:post", StringComparison.Ordinal)
            || string.Equals(howToRun, "http:get", StringComparison.Ordinal)
            || string.Equals(howToRun, "in-process-loop", StringComparison.Ordinal);
    }

    private static bool IsAllowedChatHowToRun(string howToRun)
    {
        return string.Equals(howToRun, "embedded-host", StringComparison.Ordinal)
            || string.Equals(howToRun, "built-in", StringComparison.Ordinal)
            || string.Equals(howToRun, "host-llm", StringComparison.Ordinal)
            || string.Equals(howToRun, "local-node", StringComparison.Ordinal);
    }

    private JsonObject ExecuteRunner(JsonObject payload)
    {
        if (!File.Exists(runnerPath))
        {
            throw new FileNotFoundException("The host invocation runner is missing from the runtime output.", runnerPath);
        }

        ProcessStartInfo startInfo = new("node")
        {
            WorkingDirectory = nsCodeRepoRoot,
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