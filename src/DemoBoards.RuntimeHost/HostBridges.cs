using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    private readonly InvocationExecutorConfig config;
    private string? lastInvocationJson;

    public CopilotFoundryInvocationBridge(InvocationExecutorConfig? config = null)
    {
        this.config = config ?? InvocationExecutorConfig.FromEnvironment();
    }

    public string Invoke(string refJson, string argsJson)
    {
        JsonNode? refNode = JsonNode.Parse(refJson);
        JsonNode? argsNode = JsonNode.Parse(argsJson);
        string kind = refNode?["whatToRun"]?["kind"]?.GetValue<string>() ?? "";
        string? command = config.ResolveCommand(kind);

        var record = new JsonObject
        {
            ["ref"] = refNode?.DeepClone(),
            ["args"] = argsNode?.DeepClone(),
            ["provider"] = "copilot-foundry",
            ["kind"] = kind,
        };

        if (string.IsNullOrWhiteSpace(command))
        {
            // No external executor configured — record the dispatch intent. The
            // in-process model treats this as a successful queue for the host.
            record["mode"] = "recorded";
            lastInvocationJson = record.ToJsonString();
            return "{\"dispatched\":true,\"mode\":\"recorded\"}";
        }

        var invocationPayload = new JsonObject
        {
            ["ref"] = refNode?.DeepClone(),
            ["args"] = argsNode?.DeepClone(),
        }.ToJsonString();

        ExecutorResult result = RunExecutor(command!, invocationPayload, config.TimeoutMs);
        record["mode"] = "executed";
        record["command"] = command;
        record["exitCode"] = result.ExitCode;
        record["output"] = Truncate(result.Output, 4000);
        if (!string.IsNullOrEmpty(result.Error))
        {
            record["error"] = Truncate(result.Error, 2000);
        }
        lastInvocationJson = record.ToJsonString();

        var response = new JsonObject
        {
            ["dispatched"] = result.ExitCode == 0,
            ["mode"] = "executed",
            ["exitCode"] = result.ExitCode,
        };
        if (result.ExitCode != 0)
        {
            response["error"] = string.IsNullOrEmpty(result.Error)
                ? $"executor exited with code {result.ExitCode}"
                : Truncate(result.Error, 2000);
        }
        return response.ToJsonString();
    }

    public string Describe(string refJson)
    {
        return new JsonObject
        {
            ["name"] = "copilot-foundry-host",
            ["kind"] = "chat-handler",
            ["protocolVersion"] = "1.0",
            ["ref"] = JsonNode.Parse(refJson),
            ["supports"] = new JsonArray("invoke", "describe"),
            ["executor"] = config.IsConfigured ? "configured" : "recorded",
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

    private static ExecutorResult RunExecutor(string command, string stdinPayload, int timeoutMs)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // The command comes from trusted host configuration; board-supplied
            // data is delivered only via stdin (never interpolated into argv).
            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "cmd.exe";
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(command);
            }
            else
            {
                startInfo.FileName = "/bin/sh";
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(command);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            process.StandardInput.Write(stdinPayload);
            process.StandardInput.Close();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new ExecutorResult(-1, output, "executor timed out after " + timeoutMs + "ms");
            }

            return new ExecutorResult(process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return new ExecutorResult(-1, string.Empty, ex.Message);
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value.Substring(0, max) + "…";
    }

    private readonly record struct ExecutorResult(int ExitCode, string Output, string Error);
}

/// <summary>
/// Resolves which external command (if any) backs the invocation executor. The
/// command is read from trusted host environment configuration; when none is
/// set the bridge stays in record-only mode (no process is spawned).
/// </summary>
public sealed class InvocationExecutorConfig
{
    private readonly string? copilotCommand;
    private readonly string? foundryCommand;
    private readonly string? defaultCommand;

    public InvocationExecutorConfig(string? copilotCommand, string? foundryCommand, string? defaultCommand, int timeoutMs)
    {
        this.copilotCommand = Normalize(copilotCommand);
        this.foundryCommand = Normalize(foundryCommand);
        this.defaultCommand = Normalize(defaultCommand);
        TimeoutMs = timeoutMs > 0 ? timeoutMs : 30000;
    }

    public int TimeoutMs { get; }

    public bool IsConfigured => copilotCommand is not null || foundryCommand is not null || defaultCommand is not null;

    public string? ResolveCommand(string kind)
    {
        if (string.Equals(kind, "copilot", StringComparison.OrdinalIgnoreCase) && copilotCommand is not null)
        {
            return copilotCommand;
        }

        if (string.Equals(kind, "foundry", StringComparison.OrdinalIgnoreCase) && foundryCommand is not null)
        {
            return foundryCommand;
        }

        return defaultCommand;
    }

    public static InvocationExecutorConfig FromEnvironment()
    {
        int timeoutMs = 30000;
        string? rawTimeout = Environment.GetEnvironmentVariable("DEMO_BOARDS_EXECUTOR_TIMEOUT_MS");
        if (!string.IsNullOrWhiteSpace(rawTimeout) && int.TryParse(rawTimeout, out int parsed) && parsed > 0)
        {
            timeoutMs = parsed;
        }

        return new InvocationExecutorConfig(
            Environment.GetEnvironmentVariable("DEMO_BOARDS_COPILOT_COMMAND"),
            Environment.GetEnvironmentVariable("DEMO_BOARDS_FOUNDRY_COMMAND"),
            Environment.GetEnvironmentVariable("DEMO_BOARDS_EXECUTOR_COMMAND"),
            timeoutMs);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}