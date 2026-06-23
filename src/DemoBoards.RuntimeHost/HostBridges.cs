using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace DemoBoards.RuntimeHost;

public sealed class HostStorageBridge
{
    private readonly string rootDir;
    private long idCounter;
    private long timestampCounter;

    public HostStorageBridge(string rootDir)
    {
        this.rootDir = rootDir;
        ResetStorage();
    }

    public string RootDirectory => rootDir;

    public void ResetStorage()
    {
        if (Directory.Exists(rootDir))
        {
            Directory.Delete(rootDir, recursive: true);
        }

        Directory.CreateDirectory(rootDir);
        Interlocked.Exchange(ref idCounter, 0);
        Interlocked.Exchange(ref timestampCounter, 0);
    }

    public string? KvRead(string scope, string key)
    {
        var path = ScopedKeyPath("kv", scope, key, ".json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void KvWrite(string scope, string key, string valueJson)
    {
        WriteTextAtomic(ScopedKeyPath("kv", scope, key, ".json"), valueJson);
    }

    public void KvDelete(string scope, string key)
    {
        var path = ScopedKeyPath("kv", scope, key, ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    public string KvListKeysJson(string scope, string? prefix)
    {
        return JsonSerializer.Serialize(ListDecodedKeys("kv", scope, ".json", prefix));
    }

    public string? BlobRead(string scope, string key)
    {
        var path = ScopedKeyPath("blob", scope, key, ".txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void BlobWrite(string scope, string key, string content)
    {
        WriteTextAtomic(ScopedKeyPath("blob", scope, key, ".txt"), content);
    }

    public bool BlobExists(string scope, string key)
    {
        return File.Exists(ScopedKeyPath("blob", scope, key, ".txt"));
    }

    public void BlobRemove(string scope, string key)
    {
        var path = ScopedKeyPath("blob", scope, key, ".txt");
        if (File.Exists(path)) File.Delete(path);
    }

    public string BlobListKeysJson(string scope, string? prefix)
    {
        return JsonSerializer.Serialize(ListDecodedKeys("blob", scope, ".txt", prefix));
    }

    public bool BlobRenameKey(string scope, string from, string to)
    {
        var fromPath = ScopedKeyPath("blob", scope, from, ".txt");
        if (!File.Exists(fromPath)) return false;

        var toPath = ScopedKeyPath("blob", scope, to, ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(toPath)!);
        if (File.Exists(toPath)) File.Delete(toPath);
        File.Move(fromPath, toPath);
        return true;
    }

    public string BlobKeyRefJson(string scope, string key)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "embedded-host-blob",
            value = JsonSerializer.Serialize(new { scope, key }),
        });
    }

    public string? ResolveBlobRef(string kind, string value)
    {
        if (!string.Equals(kind, "embedded-host-blob", StringComparison.Ordinal)) return null;

        var payload = JsonNode.Parse(value)?.AsObject();
        var scope = payload?["scope"]?.GetValue<string>();
        var key = payload?["key"]?.GetValue<string>();
        if (string.IsNullOrEmpty(scope) || key is null) return null;
        return BlobRead(scope, key);
    }

    public string JournalAppendJson(string scope, string payloadJson)
    {
        var entry = new JsonObject
        {
            ["id"] = NextId("journal"),
            ["payload"] = JsonNode.Parse(payloadJson),
        };

        var path = JournalPath(scope);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, entry.ToJsonString() + Environment.NewLine);
        return entry.ToJsonString();
    }

    public string JournalReadAllJson(string scope)
    {
        return JsonSerializer.Serialize(ReadJournalEntries(scope));
    }

    public string JournalReadAfterJson(string scope, string? cursor)
    {
        var entries = ReadJournalEntries(scope);
        var startIndex = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            var idx = entries.FindIndex(entry => string.Equals(entry["id"]?.GetValue<string>(), cursor, StringComparison.Ordinal));
            startIndex = idx >= 0 ? idx + 1 : 0;
        }

        var slice = entries.Skip(startIndex).ToList();
        return new JsonObject
        {
            ["entries"] = JsonNode.Parse(JsonSerializer.Serialize(slice)),
            ["newCursor"] = slice.Count > 0 ? slice[^1]["id"]?.GetValue<string>() : cursor,
        }.ToJsonString();
    }

    public void JournalClear(string scope)
    {
        var path = JournalPath(scope);
        if (File.Exists(path)) File.Delete(path);
    }

    public string QueueEnqueueJson(string scope, string bodyJson, string? dedupKey)
    {
        if (!string.IsNullOrEmpty(dedupKey) && FindQueueMessageByDedup(scope, dedupKey) is not null)
        {
            return "null";
        }

        var node = CreateQueueMessage(bodyJson, dedupKey);
        WriteQueueNode(scope, "active", node);
        return node.ToJsonString();
    }

    public string QueueLeaseJson(string scope, string optsJson)
    {
        var opts = JsonNode.Parse(optsJson)?.AsObject();
        var max = Math.Max(1, opts?["max"]?.GetValue<int?>() ?? 1);
        var visibilityMs = Math.Max(1, opts?["visibilityMs"]?.GetValue<int?>() ?? 30000);
        var leased = new JsonArray();

        foreach (var node in ReadQueueNodes(scope, "active").Take(max))
        {
            var id = node["id"]?.GetValue<string>() ?? string.Empty;
            DeleteQueueNode(scope, "active", id);
            node["attempt"] = (node["attempt"]?.GetValue<int?>() ?? 0) + 1;
            node["leaseToken"] = NextId("lease");
            node["leaseExpiresAt"] = NextTimestamp(visibilityMs);
            WriteQueueNode(scope, "leased", node);
            leased.Add(node.DeepClone());
        }

        return leased.ToJsonString();
    }

    public bool QueueAck(string scope, string messageId, string leaseToken)
    {
        var node = ReadQueueNode(scope, "leased", messageId);
        if (!LeaseMatches(node, leaseToken)) return false;
        DeleteQueueNode(scope, "leased", messageId);
        return true;
    }

    public bool QueueNack(string scope, string messageId, string leaseToken, bool dead, string? reason)
    {
        var node = ReadQueueNode(scope, "leased", messageId);
        if (!LeaseMatches(node, leaseToken)) return false;

        DeleteQueueNode(scope, "leased", messageId);
        node!["leaseToken"] = null;
        node["leaseExpiresAt"] = null;
        if (dead)
        {
            node["reason"] = reason;
            WriteQueueNode(scope, "dead", node);
            return true;
        }

        WriteQueueNode(scope, "active", node);
        return true;
    }

    public string QueuePeekActiveJson(string scope, string? prefix)
    {
        return QueuePeekJson(scope, "active", prefix);
    }

    public string QueuePeekDeadLetterJson(string scope, string? prefix)
    {
        return QueuePeekJson(scope, "dead", prefix);
    }

    public string QueueStageJson(string scope, string bodyJson, string? dedupKey)
    {
        if (!string.IsNullOrEmpty(dedupKey) && FindQueueMessageByDedup(scope, dedupKey) is not null)
        {
            return "null";
        }

        var node = CreateQueueMessage(bodyJson, dedupKey);
        WriteQueueNode(scope, "staged", node);
        return node.ToJsonString();
    }

    public bool QueueCommitStaged(string scope, string messageId)
    {
        var node = ReadQueueNode(scope, "staged", messageId);
        if (node is null) return false;

        DeleteQueueNode(scope, "staged", messageId);
        node["enqueuedAt"] = NextTimestamp(0);
        node["attempt"] = 0;
        WriteQueueNode(scope, "active", node);
        return true;
    }

    public bool QueueDiscardStaged(string scope, string messageId, string? reason)
    {
        var node = ReadQueueNode(scope, "staged", messageId);
        if (node is null) return false;

        DeleteQueueNode(scope, "staged", messageId);
        node["reason"] = reason;
        WriteQueueNode(scope, "dead", node);
        return true;
    }

    public string QueuePeekStagedJson(string scope, string? prefix)
    {
        return QueuePeekJson(scope, "staged", prefix);
    }

    public string? MetaGet(string bucket, string scope, string key)
    {
        var path = ScopedKeyPath($"meta-{bucket}", scope, key, ".json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void MetaSet(string bucket, string scope, string key, string valueJson)
    {
        WriteTextAtomic(ScopedKeyPath($"meta-{bucket}", scope, key, ".json"), valueJson);
    }

    private static bool LeaseMatches(JsonObject? node, string leaseToken)
    {
        return node is not null && string.Equals(node["leaseToken"]?.GetValue<string>(), leaseToken, StringComparison.Ordinal);
    }

    private JsonObject CreateQueueMessage(string bodyJson, string? dedupKey)
    {
        var node = new JsonObject
        {
            ["id"] = NextId("queue"),
            ["body"] = JsonNode.Parse(bodyJson),
            ["enqueuedAt"] = NextTimestamp(0),
            ["attempt"] = 0,
        };
        if (!string.IsNullOrEmpty(dedupKey)) node["dedupKey"] = dedupKey;
        return node;
    }

    private JsonObject? FindQueueMessageByDedup(string scope, string dedupKey)
    {
        foreach (var state in new[] { "active", "leased", "staged" })
        {
            var hit = ReadQueueNodes(scope, state)
                .FirstOrDefault(node => string.Equals(node["dedupKey"]?.GetValue<string>(), dedupKey, StringComparison.Ordinal));
            if (hit is not null) return hit;
        }

        return null;
    }

    private string QueuePeekJson(string scope, string state, string? prefix)
    {
        var nodes = ReadQueueNodes(scope, state)
            .Where(node => string.IsNullOrEmpty(prefix) || (node["id"]?.GetValue<string>() ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        return JsonSerializer.Serialize(nodes);
    }

    private List<JsonObject> ReadQueueNodes(string scope, string state)
    {
        var dir = QueueDir(scope, state);
        if (!Directory.Exists(dir)) return new List<JsonObject>();

        return Directory.EnumerateFiles(dir, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))?.AsObject())
            .Where(node => node is not null)
            .Cast<JsonObject>()
            .ToList();
    }

    private JsonObject? ReadQueueNode(string scope, string state, string messageId)
    {
        var path = QueuePath(scope, state, messageId);
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
    }

    private void WriteQueueNode(string scope, string state, JsonObject node)
    {
        var messageId = node["id"]?.GetValue<string>() ?? throw new InvalidOperationException("queue message missing id");
        WriteTextAtomic(QueuePath(scope, state, messageId), node.ToJsonString());
    }

    private void DeleteQueueNode(string scope, string state, string messageId)
    {
        var path = QueuePath(scope, state, messageId);
        if (File.Exists(path)) File.Delete(path);
    }

    private List<JsonObject> ReadJournalEntries(string scope)
    {
        var path = JournalPath(scope);
        if (!File.Exists(path)) return new List<JsonObject>();

        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonNode.Parse(line)?.AsObject())
            .Where(node => node is not null)
            .Cast<JsonObject>()
            .ToList();
    }

    private List<string> ListDecodedKeys(string kind, string scope, string extension, string? prefix)
    {
        var dir = ScopeDir(kind, scope);
        if (!Directory.Exists(dir)) return new List<string>();

        return Directory.EnumerateFiles(dir, $"*{extension}")
            .Select(path => DecodePathSegment(Path.GetFileNameWithoutExtension(path)))
            .Where(key => string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
    }

    private string JournalPath(string scope)
    {
        return Path.Combine(rootDir, "journal", EncodePathSegment(scope) + ".jsonl");
    }

    private string QueueDir(string scope, string state)
    {
        return Path.Combine(rootDir, "queue", EncodePathSegment(scope), state);
    }

    private string QueuePath(string scope, string state, string messageId)
    {
        return Path.Combine(QueueDir(scope, state), EncodePathSegment(messageId) + ".json");
    }

    private string ScopedKeyPath(string kind, string scope, string key, string extension)
    {
        return Path.Combine(ScopeDir(kind, scope), EncodePathSegment(key) + extension);
    }

    private string ScopeDir(string kind, string scope)
    {
        return Path.Combine(rootDir, kind, EncodePathSegment(scope));
    }

    private string NextId(string prefix)
    {
        return $"{prefix}-{Interlocked.Increment(ref idCounter):D6}";
    }

    private string NextTimestamp(int extraMs)
    {
        var ticks = Interlocked.Increment(ref timestampCounter);
        return DateTimeOffset.UnixEpoch.AddSeconds(ticks).AddMilliseconds(extraMs).ToString("O", CultureInfo.InvariantCulture);
    }

    private static void WriteTextAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tempPath, path);
    }

    private static string EncodePathSegment(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string DecodePathSegment(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - (normalized.Length % 4)) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }
}

public sealed class CopilotFoundryInvocationBridge
{
    private readonly HostControlfaceBridge controlfaceBridge;
    private string serverUrl = string.Empty;
    private string mcpServerUrl = string.Empty;
    private readonly string runnerPath;
    private readonly string repoRoot;
    private string? lastInvocationJson;

    public CopilotFoundryInvocationBridge(HostControlfaceBridge controlfaceBridge, string serverUrl)
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