using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DemoBoards.RuntimeHost;

public sealed class HostControlfaceBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string managedBoardsDirectory;
    private readonly string deprecatedManagedBoardsDirectory;
    private readonly string sampleTemplateDirectory;
    private readonly string sampleTemplateIndexPath;
    private readonly string agentfaceManifestPath;

    public HostControlfaceBridge(string runtimeRootDirectory, string assetBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(runtimeRootDirectory))
        {
            throw new ArgumentException("Runtime root directory is required.", nameof(runtimeRootDirectory));
        }

        if (string.IsNullOrWhiteSpace(assetBaseDirectory))
        {
            throw new ArgumentException("Asset base directory is required.", nameof(assetBaseDirectory));
        }

        managedBoardsDirectory = Path.Combine(runtimeRootDirectory, "controlface-state", "managed-boards");
        deprecatedManagedBoardsDirectory = Path.Combine(runtimeRootDirectory, "controlface-state", "deprecated-managed-boards");
        sampleTemplateDirectory = Path.Combine(assetBaseDirectory, "sample-card-templates");
        sampleTemplateIndexPath = Path.Combine(sampleTemplateDirectory, "_index.json");
        agentfaceManifestPath = Path.Combine(assetBaseDirectory, "agentface.tools.json");

        Directory.CreateDirectory(managedBoardsDirectory);
        Directory.CreateDirectory(deprecatedManagedBoardsDirectory);
    }

    public string ListManagedBoardStatesJson()
    {
        var items = Directory.Exists(managedBoardsDirectory)
            ? Directory.EnumerateFiles(managedBoardsDirectory, "*.json")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(ReadManagedBoardState)
                .Where(node => node is not null)
                .Cast<JsonObject>()
                .ToArray()
            : Array.Empty<JsonObject>();

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    public string ListBoardContainerRecordsJson()
    {
        JsonObject[] items = Directory.Exists(managedBoardsDirectory)
            ? Directory.EnumerateFiles(managedBoardsDirectory, "*.json")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(ReadManagedBoardState)
                .Where(node => node is not null)
                .Select(node => node!["board"] as JsonObject)
                .Where(node => node is not null)
                .Cast<JsonObject>()
                .ToArray()
            : Array.Empty<JsonObject>();

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    public string? GetManagedBoardStateJson(string boardId)
    {
        string path = GetManagedBoardPath(boardId);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public string? GetBoardContainerRecordJson(string boardId)
    {
        JsonObject? state = GetManagedBoardStateObject(boardId);
        return (state?["board"] as JsonObject)?.ToJsonString(JsonOptions);
    }

    public bool HasBoardContainerRecord(string boardId)
    {
        string path = GetManagedBoardPath(boardId);
        return File.Exists(path);
    }

    public void SaveManagedBoardStateJson(string boardId, string stateJson)
    {
        JsonNode? parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(stateJson) ? "{}" : stateJson);
        if (parsed is not JsonObject stateObject)
        {
            throw new InvalidOperationException("Managed board state must be a JSON object.");
        }

        WriteTextAtomic(GetManagedBoardPath(boardId), stateObject.ToJsonString(JsonOptions));
    }

    public void PutBoardContainerRecordJson(string boardId, string recordJson)
    {
        string normalizedBoardId = NormalizeRequiredKey(boardId, "Board id is required.");
        string path = GetManagedBoardPath(normalizedBoardId);
        if (File.Exists(path))
        {
            throw new InvalidOperationException($"Board '{normalizedBoardId}' already exists.");
        }

        JsonObject record = ParseRequiredJsonObject(recordJson, "Board container record must be a JSON object.");
        JsonObject state = CreateManagedBoardState(normalizedBoardId, record, null);
        WriteTextAtomic(path, state.ToJsonString(JsonOptions));
    }

    public void SetBoardContainerRecordJson(string boardId, string recordJson)
    {
        string normalizedBoardId = NormalizeRequiredKey(boardId, "Board id is required.");
        JsonObject record = ParseRequiredJsonObject(recordJson, "Board container record must be a JSON object.");
        JsonObject? existingState = GetManagedBoardStateObject(normalizedBoardId);
        JsonNode? layout = existingState?["layout"]?.DeepClone();
        JsonObject state = CreateManagedBoardState(normalizedBoardId, record, layout);
        WriteTextAtomic(GetManagedBoardPath(normalizedBoardId), state.ToJsonString(JsonOptions));
    }

    public string? GetBoardContainerLayoutJson(string boardId)
    {
        JsonObject? state = GetManagedBoardStateObject(boardId);
        JsonNode? layout = state?["layout"];
        return layout is null ? null : layout.ToJsonString(JsonOptions);
    }

    public void SetBoardContainerLayoutJson(string boardId, string layoutJson)
    {
        string normalizedBoardId = NormalizeRequiredKey(boardId, "Board id is required.");
        JsonObject state = EnsureManagedBoardStateObject(normalizedBoardId);
        state["layout"] = string.IsNullOrWhiteSpace(layoutJson) ? null : JsonNode.Parse(layoutJson);
        WriteTextAtomic(GetManagedBoardPath(normalizedBoardId), state.ToJsonString(JsonOptions));
    }

    public void RemoveBoardContainerLayout(string boardId)
    {
        string normalizedBoardId = NormalizeRequiredKey(boardId, "Board id is required.");
        JsonObject state = EnsureManagedBoardStateObject(normalizedBoardId);
        state["layout"] = null;
        WriteTextAtomic(GetManagedBoardPath(normalizedBoardId), state.ToJsonString(JsonOptions));
    }

    public string? ArchiveBoardContainerJson(string boardId)
    {
        string? archivedJson = DeprecateManagedBoardStateJson(boardId);
        if (string.IsNullOrWhiteSpace(archivedJson))
        {
            return null;
        }

        JsonObject payload = ParseRequiredJsonObject(archivedJson, "Archived board container payload must be a JSON object.");
        JsonObject? state = payload["state"] as JsonObject;
        JsonObject archived = new()
        {
            ["record"] = state?["board"]?.DeepClone(),
            ["layout"] = state?["layout"]?.DeepClone(),
            ["archiveId"] = payload["archiveId"]?.GetValue<string>() ?? string.Empty,
            ["archiveRecordPath"] = payload["archiveRecordPath"]?.GetValue<string>() ?? string.Empty,
            ["archiveWorkspaceDir"] = payload["archiveWorkspaceDir"]?.GetValue<string>() ?? string.Empty,
        };

        return archived.ToJsonString(JsonOptions);
    }

    public string ListSampleTemplateEntriesJson()
    {
        JsonObject manifest = ReadSampleTemplateManifest();
        JsonArray entries = manifest["entries"] as JsonArray ?? new JsonArray();
        return entries.ToJsonString(JsonOptions);
    }

    public string GetSampleTemplateEnvelopeJson(string key)
    {
        string normalizedKey = NormalizeRequiredKey(key, "Template key is required.");
        JsonObject manifest = ReadSampleTemplateManifest();
        JsonArray entries = manifest["entries"] as JsonArray ?? new JsonArray();

        JsonObject? entry = entries
            .Select(node => node as JsonObject)
            .FirstOrDefault(candidate => string.Equals(candidate?["key"]?.GetValue<string>(), normalizedKey, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new InvalidOperationException($"Sample template '{normalizedKey}' not found.");
        }

        string fileName = NormalizeRequiredKey(entry["fileName"]?.GetValue<string>(), "Template fileName is required.");
        string templatePath = Path.Combine(sampleTemplateDirectory, Path.GetFileName(fileName));
        if (!File.Exists(templatePath))
        {
            throw new InvalidOperationException($"Sample template '{normalizedKey}' is missing.");
        }

        JsonNode? payload = JsonNode.Parse(File.ReadAllText(templatePath));
        var envelope = new JsonObject
        {
            ["key"] = normalizedKey,
            ["label"] = entry["label"]?.GetValue<string>() ?? normalizedKey,
            ["description"] = entry["description"]?.GetValue<string>() ?? string.Empty,
            ["payload"] = payload
        };

        return envelope.ToJsonString(JsonOptions);
    }

    public string GetAgentfaceToolsManifestJson()
    {
        if (!File.Exists(agentfaceManifestPath))
        {
            throw new InvalidOperationException("Agentface tools manifest is missing.");
        }

        return File.ReadAllText(agentfaceManifestPath);
    }

    public string? DeprecateManagedBoardStateJson(string boardId)
    {
        string sourcePath = GetManagedBoardPath(boardId);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        string stateJson = File.ReadAllText(sourcePath);
        JsonNode? parsed = JsonNode.Parse(stateJson);
        if (parsed is not JsonObject stateObject)
        {
            throw new InvalidOperationException("Managed board state is invalid.");
        }

        (string archiveId, string archiveRecordPath) = ReserveArchiveRecordPath(boardId);
        File.Move(sourcePath, archiveRecordPath);

        JsonObject payload = new()
        {
            ["state"] = stateObject,
            ["archiveId"] = archiveId,
            ["archiveRecordPath"] = archiveRecordPath,
            ["archiveWorkspaceDir"] = string.Empty,
        };

        return payload.ToJsonString(JsonOptions);
    }

    private JsonObject EnsureManagedBoardStateObject(string boardId)
    {
        JsonObject? state = GetManagedBoardStateObject(boardId);
        if (state is null)
        {
            throw new InvalidOperationException($"Board '{boardId}' not found.");
        }

        return state;
    }

    private JsonObject? GetManagedBoardStateObject(string boardId)
    {
        string path = GetManagedBoardPath(boardId);
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
    }

    private JsonObject? ReadManagedBoardState(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private JsonObject ReadSampleTemplateManifest()
    {
        if (!File.Exists(sampleTemplateIndexPath))
        {
            throw new InvalidOperationException("Sample template catalog is missing.");
        }

        JsonNode? parsed = JsonNode.Parse(File.ReadAllText(sampleTemplateIndexPath));
        if (parsed is not JsonObject manifest)
        {
            throw new InvalidOperationException("Sample template catalog is invalid.");
        }

        return manifest;
    }

    private static JsonObject ParseRequiredJsonObject(string json, string errorMessage)
    {
        JsonNode? parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (parsed is not JsonObject jsonObject)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return jsonObject;
    }

    private static JsonObject CreateManagedBoardState(string boardId, JsonObject record, JsonNode? layout)
    {
        record["id"] = NormalizeRequiredKey(record["id"]?.GetValue<string>() ?? boardId, "Board id is required.");
        JsonNode? metadata = record["metadata"];
        if (metadata is not JsonObject)
        {
            record["metadata"] = new JsonObject();
        }

        JsonNode? ui = record["ui"];
        if (ui is not JsonObject)
        {
            record["ui"] = new JsonObject();
        }

        return new JsonObject
        {
            ["boardId"] = boardId,
            ["board"] = record,
            ["layout"] = layout?.DeepClone(),
        };
    }

    private string GetManagedBoardPath(string boardId)
    {
        string normalized = NormalizeRequiredKey(boardId, "Board id is required.");
        Span<char> buffer = stackalloc char[normalized.Length];
        for (int index = 0; index < normalized.Length; index++)
        {
            char value = normalized[index];
            buffer[index] = Array.IndexOf(Path.GetInvalidFileNameChars(), value) >= 0 ? '_' : value;
        }

        return Path.Combine(managedBoardsDirectory, new string(buffer) + ".json");
    }

    private static string NormalizeRequiredKey(string? value, string errorMessage)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return normalized;
    }

    private (string ArchiveId, string ArchiveRecordPath) ReserveArchiveRecordPath(string boardId)
    {
        string normalized = NormalizeRequiredKey(boardId, "Board id is required.");
        Directory.CreateDirectory(deprecatedManagedBoardsDirectory);

        string stamp = DateTime.Now.ToString("MMdd-HHmmss");
        string suffix = string.Empty;
        int attempt = 1;
        while (true)
        {
            string archiveId = normalized + "-" + stamp + suffix;
            string archiveRecordPath = Path.Combine(deprecatedManagedBoardsDirectory, archiveId + ".json");
            if (!File.Exists(archiveRecordPath))
            {
                return (archiveId, archiveRecordPath);
            }

            attempt += 1;
            suffix = "-" + attempt;
        }
    }

    private static void WriteTextAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }
}