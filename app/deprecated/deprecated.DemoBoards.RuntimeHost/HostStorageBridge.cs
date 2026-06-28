using System;
using System.Collections.Generic;
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
        if (string.Equals(kind, "fs-path", StringComparison.Ordinal))
        {
            return File.Exists(value) ? File.ReadAllText(value) : null;
        }

        if (!string.Equals(kind, "embedded-host-blob", StringComparison.Ordinal)) return null;

        var payload = JsonNode.Parse(value)?.AsObject();
        var scope = payload?["scope"]?.GetValue<string>();
        var key = payload?["key"]?.GetValue<string>();
        if (string.IsNullOrEmpty(scope) || key is null) return null;
        return BlobRead(scope, key);
    }

    // ------------------------------------------------------------------------
    // Shared filesystem blob store (kind = "fs-path").
    //
    // Used for the board's fetched-sources store so that an out-of-process
    // node board-worker (which only understands `fs-path` refs) and the embedded
    // host read/write the SAME on-disk file. keyRef returns an absolute fs-path
    // ref; read/write map the logical (scope, key) deterministically to that path.
    // ------------------------------------------------------------------------

    public string SharedBlobKeyRefJson(string scope, string key)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "fs-path",
            value = SharedBlobAbsolutePath(scope, key),
        });
    }

    public string? SharedBlobRead(string scope, string key)
    {
        var path = SharedBlobAbsolutePath(scope, key);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void SharedBlobWrite(string scope, string key, string content)
    {
        var path = SharedBlobAbsolutePath(scope, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public bool SharedBlobExists(string scope, string key)
    {
        return File.Exists(SharedBlobAbsolutePath(scope, key));
    }

    public void SharedBlobRemove(string scope, string key)
    {
        var path = SharedBlobAbsolutePath(scope, key);
        if (File.Exists(path)) File.Delete(path);
    }

    public string SharedBlobListKeysJson(string scope, string? prefix)
    {
        var scopeDir = Path.Combine(rootDir, "shared-fs", EncodePathSegment(scope));
        var keys = new List<string>();
        if (Directory.Exists(scopeDir))
        {
            foreach (var file in Directory.EnumerateFiles(scopeDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(scopeDir, file).Replace(Path.DirectorySeparatorChar, '/');
                if (string.IsNullOrEmpty(prefix) || relative.StartsWith(prefix, StringComparison.Ordinal))
                {
                    keys.Add(relative);
                }
            }
        }

        return JsonSerializer.Serialize(keys);
    }

    public bool SharedBlobRenameKey(string scope, string from, string to)
    {
        var fromPath = SharedBlobAbsolutePath(scope, from);
        if (!File.Exists(fromPath)) return false;

        var toPath = SharedBlobAbsolutePath(scope, to);
        Directory.CreateDirectory(Path.GetDirectoryName(toPath)!);
        if (File.Exists(toPath)) File.Delete(toPath);
        File.Move(fromPath, toPath);
        return true;
    }

    private string SharedBlobAbsolutePath(string scope, string key)
    {
        var scopeDir = Path.GetFullPath(Path.Combine(rootDir, "shared-fs", EncodePathSegment(scope)));
        var relativeKey = key.Replace('\\', '/').TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(scopeDir, relativeKey));
        if (!resolved.StartsWith(scopeDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(resolved, scopeDir, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Shared blob key '{key}' escapes its scope directory.");
        }

        return resolved;
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
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueueEnqueueJson(fsQueueRoot, bodyJson, dedupKey);
        }

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
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueueLeaseJson(fsQueueRoot, optsJson);
        }

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
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueueAck(fsQueueRoot, messageId, leaseToken);
        }

        var node = ReadQueueNode(scope, "leased", messageId);
        if (!LeaseMatches(node, leaseToken)) return false;
        DeleteQueueNode(scope, "leased", messageId);
        return true;
    }

    public bool QueueNack(string scope, string messageId, string leaseToken, bool dead, string? reason)
    {
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueueNack(fsQueueRoot, messageId, leaseToken, dead, reason);
        }

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
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueuePeekJson(fsQueueRoot, "active", prefix);
        }

        return QueuePeekJson(scope, "active", prefix);
    }

    public string QueuePeekDeadLetterJson(string scope, string? prefix)
    {
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueuePeekJson(fsQueueRoot, "dead", prefix);
        }

        return QueuePeekJson(scope, "dead", prefix);
    }

    public string QueueStageJson(string scope, string bodyJson, string? dedupKey)
    {
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueueStageJson(fsQueueRoot, bodyJson, dedupKey);
        }

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
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueueCommitStaged(fsQueueRoot, messageId);
        }

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
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueueDiscardStaged(fsQueueRoot, messageId, reason);
        }

        var node = ReadQueueNode(scope, "staged", messageId);
        if (node is null) return false;

        DeleteQueueNode(scope, "staged", messageId);
        node["reason"] = reason;
        WriteQueueNode(scope, "dead", node);
        return true;
    }

    public string QueuePeekStagedJson(string scope, string? prefix)
    {
        if (TryGetFsQueueRoot(scope, out var fsQueueRoot))
        {
            return FsQueuePeekJson(fsQueueRoot, "staged", prefix);
        }

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

    private bool TryGetFsQueueRoot(string scope, out string queueRoot)
    {
        queueRoot = string.Empty;
        if (!TryParseKindValueRef(scope, out var kind, out var value)) return false;
        if (!string.Equals(kind, "fs-path", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(value)) return false;
        queueRoot = Path.GetFullPath(value);
        return true;
    }

    private static bool TryParseKindValueRef(string raw, out string kind, out string value)
    {
        kind = string.Empty;
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        try
        {
            JsonNode? node;
            if (trimmed.StartsWith("b64:", StringComparison.Ordinal))
            {
                var encoded = trimmed.Substring(4).Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight(encoded.Length + ((4 - (encoded.Length % 4)) % 4), '=');
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                node = JsonNode.Parse(json);
            }
            else if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                node = JsonNode.Parse(trimmed);
            }
            else
            {
                return false;
            }

            var obj = node?.AsObject();
            var parsedKind = obj?["kind"]?.GetValue<string>();
            var parsedValue = obj?["value"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(parsedKind) || string.IsNullOrWhiteSpace(parsedValue)) return false;
            kind = parsedKind;
            value = parsedValue;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string FsQueueEnqueueJson(string queueRoot, string bodyJson, string? dedupKey)
    {
        if (!string.IsNullOrEmpty(dedupKey) && FindFsQueueMessageByDedup(queueRoot, dedupKey) is not null)
        {
            return "null";
        }

        var node = CreateQueueMessage(bodyJson, dedupKey);
        node["activeOrderKey"] = NextFsActiveOrderKey(node["enqueuedAt"]?.GetValue<string>());
        WriteTextAtomic(FsQueueActivePath(queueRoot, node), node.ToJsonString());
        return node.ToJsonString();
    }

    private string FsQueueLeaseJson(string queueRoot, string optsJson)
    {
        ReviveExpiredFsLeases(queueRoot);
        var opts = JsonNode.Parse(optsJson)?.AsObject();
        var max = Math.Max(1, opts?["max"]?.GetValue<int?>() ?? 1);
        var visibilityMs = Math.Max(1, opts?["visibilityMs"]?.GetValue<int?>() ?? 30000);
        var leased = new JsonArray();

        foreach (var path in ListFsQueueStateFiles(queueRoot, "active").Take(max))
        {
            var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            if (node is null) continue;

            var messageId = node["id"]?.GetValue<string>() ?? string.Empty;
            var claimedPath = FsQueueLeasedPath(queueRoot, messageId);
            try
            {
                if (File.Exists(claimedPath)) File.Delete(claimedPath);
                Directory.CreateDirectory(Path.GetDirectoryName(claimedPath)!);
                File.Move(path, claimedPath);
            }
            catch
            {
                continue;
            }

            node["attempt"] = (node["attempt"]?.GetValue<int?>() ?? 0) + 1;
            node["leaseToken"] = NextId("lease");
            node["leaseExpiresAt"] = DateTimeOffset.UtcNow.AddMilliseconds(visibilityMs).ToString("O", CultureInfo.InvariantCulture);
            WriteTextAtomic(claimedPath, node.ToJsonString());
            leased.Add(node.DeepClone());
        }

        return leased.ToJsonString();
    }

    private bool FsQueueAck(string queueRoot, string messageId, string leaseToken)
    {
        var path = FsQueueLeasedPath(queueRoot, messageId);
        var node = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
        if (!LeaseMatches(node, leaseToken)) return false;
        var donePath = FsQueueDonePath(queueRoot, messageId);
        Directory.CreateDirectory(Path.GetDirectoryName(donePath)!);
        if (File.Exists(donePath)) File.Delete(donePath);
        File.Move(path, donePath);
        return true;
    }

    private bool FsQueueNack(string queueRoot, string messageId, string leaseToken, bool dead, string? reason)
    {
        var path = FsQueueLeasedPath(queueRoot, messageId);
        var node = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
        if (!LeaseMatches(node, leaseToken) || node is null) return false;

        var nextNode = new JsonObject
        {
            ["id"] = node["id"]?.DeepClone(),
            ["body"] = node["body"]?.DeepClone(),
            ["enqueuedAt"] = node["enqueuedAt"]?.DeepClone(),
            ["attempt"] = node["attempt"]?.DeepClone(),
        };
        if (node["activeOrderKey"] is not null) nextNode["activeOrderKey"] = node["activeOrderKey"]?.DeepClone();
        if (node["dedupKey"] is not null) nextNode["dedupKey"] = node["dedupKey"]?.DeepClone();

        if (dead)
        {
            nextNode["reason"] = reason;
            WriteTextAtomic(FsQueueDeadPath(queueRoot, messageId), nextNode.ToJsonString());
        }
        else
        {
            WriteTextAtomic(FsQueueActivePath(queueRoot, nextNode), nextNode.ToJsonString());
        }

        File.Delete(path);
        return true;
    }

    private string FsQueuePeekJson(string queueRoot, string state, string? prefix)
    {
        if (string.Equals(state, "active", StringComparison.Ordinal))
        {
            ReviveExpiredFsLeases(queueRoot);
        }

        var nodes = ListFsQueueStateFiles(queueRoot, state)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))?.AsObject())
            .Where(node => node is not null)
            .Cast<JsonObject>()
            .Where(node => string.IsNullOrEmpty(prefix) || (node["id"]?.GetValue<string>() ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        return JsonSerializer.Serialize(nodes);
    }

    private string FsQueueStageJson(string queueRoot, string bodyJson, string? dedupKey)
    {
        if (!string.IsNullOrEmpty(dedupKey) && FindFsQueueMessageByDedup(queueRoot, dedupKey) is not null)
        {
            return "null";
        }

        var node = CreateQueueMessage(bodyJson, dedupKey);
        WriteTextAtomic(FsQueueStagedPath(queueRoot, node["id"]?.GetValue<string>() ?? string.Empty), node.ToJsonString());
        return node.ToJsonString();
    }

    private bool FsQueueCommitStaged(string queueRoot, string messageId)
    {
        var path = FsQueueStagedPath(queueRoot, messageId);
        var node = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
        if (node is null) return false;

        var enqueuedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        node["attempt"] = 0;
        node["enqueuedAt"] = enqueuedAt;
        node["activeOrderKey"] = NextFsActiveOrderKey(enqueuedAt);
        WriteTextAtomic(FsQueueActivePath(queueRoot, node), node.ToJsonString());
        File.Delete(path);
        return true;
    }

    private bool FsQueueDiscardStaged(string queueRoot, string messageId, string? reason)
    {
        var path = FsQueueStagedPath(queueRoot, messageId);
        var node = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
        if (node is null) return false;

        var discarded = new JsonObject
        {
            ["id"] = node["id"]?.DeepClone(),
            ["body"] = node["body"]?.DeepClone(),
            ["enqueuedAt"] = node["enqueuedAt"]?.DeepClone(),
            ["attempt"] = node["attempt"]?.DeepClone(),
            ["reason"] = reason,
        };
        if (node["dedupKey"] is not null) discarded["dedupKey"] = node["dedupKey"]?.DeepClone();
        WriteTextAtomic(FsQueueDeadPath(queueRoot, messageId), discarded.ToJsonString());
        File.Delete(path);
        return true;
    }

    private JsonObject? FindFsQueueMessageByDedup(string queueRoot, string dedupKey)
    {
        ReviveExpiredFsLeases(queueRoot);
        foreach (var state in new[] { "active", "leased", "staged" })
        {
            foreach (var path in ListFsQueueStateFiles(queueRoot, state))
            {
                var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
                if (string.Equals(node?["dedupKey"]?.GetValue<string>(), dedupKey, StringComparison.Ordinal))
                {
                    return node;
                }
            }
        }

        return null;
    }

    private void ReviveExpiredFsLeases(string queueRoot)
    {
        foreach (var path in ListFsQueueStateFiles(queueRoot, "leased"))
        {
            var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            var expiresAt = node?["leaseExpiresAt"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(expiresAt)) continue;
            if (!DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry)) continue;
            if (expiry > DateTimeOffset.UtcNow) continue;

            var revived = new JsonObject
            {
                ["id"] = node?["id"]?.DeepClone(),
                ["body"] = node?["body"]?.DeepClone(),
                ["enqueuedAt"] = node?["enqueuedAt"]?.DeepClone(),
                ["attempt"] = node?["attempt"]?.DeepClone(),
            };
            if (node?["activeOrderKey"] is not null) revived["activeOrderKey"] = node["activeOrderKey"]?.DeepClone();
            if (node?["dedupKey"] is not null) revived["dedupKey"] = node["dedupKey"]?.DeepClone();
            WriteTextAtomic(FsQueueActivePath(queueRoot, revived), revived.ToJsonString());
            File.Delete(path);
        }
    }

    private IEnumerable<string> ListFsQueueStateFiles(string queueRoot, string state)
    {
        var dir = FsQueueStateDir(queueRoot, state);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, "*.json").OrderBy(path => path, StringComparer.Ordinal);
    }

    private string NextFsActiveOrderKey(string? enqueuedAt)
    {
        var stamp = (enqueuedAt ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Replace(':', '-').Replace('.', '-');
        var seq = Interlocked.Increment(ref idCounter).ToString("D6", CultureInfo.InvariantCulture);
        return $"{stamp}-{seq}";
    }

    private string FsQueueActivePath(string queueRoot, JsonObject node)
    {
        var messageId = node["id"]?.GetValue<string>() ?? throw new InvalidOperationException("queue message missing id");
        var orderKey = node["activeOrderKey"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(orderKey))
        {
            orderKey = NextFsActiveOrderKey(node["enqueuedAt"]?.GetValue<string>());
            node["activeOrderKey"] = orderKey;
        }
        return Path.Combine(FsQueueStateDir(queueRoot, "active"), $"{orderKey}-{messageId}.json");
    }

    private string FsQueueLeasedPath(string queueRoot, string messageId)
        => Path.Combine(FsQueueStateDir(queueRoot, "leased"), $"{messageId}.json");

    private string FsQueueDonePath(string queueRoot, string messageId)
        => Path.Combine(FsQueueStateDir(queueRoot, "done"), $"{messageId}.json");

    private string FsQueueDeadPath(string queueRoot, string messageId)
        => Path.Combine(FsQueueStateDir(queueRoot, "dead"), $"{messageId}.json");

    private string FsQueueStagedPath(string queueRoot, string messageId)
        => Path.Combine(FsQueueStateDir(queueRoot, "staged"), $"{messageId}.json");

    private static string FsQueueStateDir(string queueRoot, string state)
        => Path.Combine(queueRoot, state);

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