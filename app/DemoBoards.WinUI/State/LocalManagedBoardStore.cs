using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DemoBoards_WinUI.State;

public sealed class LocalManagedBoardStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string storageDirectory;

    public LocalManagedBoardStore(string appStorageDirectory)
    {
        if (string.IsNullOrWhiteSpace(appStorageDirectory))
        {
            throw new ArgumentException("Storage directory is required.", nameof(appStorageDirectory));
        }

        storageDirectory = Path.Combine(appStorageDirectory, "managed-boards");
        Directory.CreateDirectory(storageDirectory);
    }

    public async Task<ManagedBoardConfigState> GetAsync(string boardId)
    {
        string normalizedBoardId = NormalizeBoardId(boardId);
        LocalManagedBoardRecord record = await ReadRecordAsync(normalizedBoardId).ConfigureAwait(false)
            ?? CreateDefaultRecord(normalizedBoardId);
        return record.ToManagedState();
    }

    public async Task<ManagedBoardConfigState> SaveAsync(string boardId, string rawUiJson, string rawMetadataJson, string rawLayoutJson, string? currentRawBoardJson = null)
    {
        string normalizedBoardId = NormalizeBoardId(boardId);
        JsonObject ui = ParseObject(rawUiJson, "ui");
        JsonObject metadata = ParseObject(rawMetadataJson, "metadata");
        JsonNode? layout = ParseLayout(rawLayoutJson);
        JsonObject board = MergeBoard(normalizedBoardId, currentRawBoardJson, ui, metadata);

        var record = new LocalManagedBoardRecord(normalizedBoardId, board, layout);
        await WriteRecordAsync(record).ConfigureAwait(false);
        return record.ToManagedState();
    }

    public async Task SaveLayoutAsync(string boardId, BoardCanvasLayoutState layoutState)
    {
        string normalizedBoardId = NormalizeBoardId(boardId);
        LocalManagedBoardRecord record = await ReadRecordAsync(normalizedBoardId).ConfigureAwait(false)
            ?? CreateDefaultRecord(normalizedBoardId);
        JsonNode layout = BuildLayoutNode(layoutState);
        LocalManagedBoardRecord updated = record with { Layout = layout };
        await WriteRecordAsync(updated).ConfigureAwait(false);
    }

    private async Task<LocalManagedBoardRecord?> ReadRecordAsync(string boardId)
    {
        string path = GetPath(boardId);
        if (!File.Exists(path))
        {
            return null;
        }

        string raw = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        JsonNode? root = JsonNode.Parse(raw);
        if (root is not JsonObject rootObject)
        {
            return null;
        }

        JsonObject board = rootObject["board"] as JsonObject ?? CreateDefaultBoard(boardId);
        JsonNode? layout = rootObject["layout"]?.DeepClone();
        return new LocalManagedBoardRecord(boardId, board, layout);
    }

    private async Task WriteRecordAsync(LocalManagedBoardRecord record)
    {
        var root = new JsonObject
        {
            ["boardId"] = record.BoardId,
            ["board"] = record.Board.DeepClone(),
            ["layout"] = record.Layout?.DeepClone()
        };

        await File.WriteAllTextAsync(GetPath(record.BoardId), root.ToJsonString(JsonOptions)).ConfigureAwait(false);
    }

    private string GetPath(string boardId)
    {
        Span<char> buffer = stackalloc char[boardId.Length];
        for (int index = 0; index < boardId.Length; index++)
        {
            char value = boardId[index];
            buffer[index] = Array.IndexOf(Path.GetInvalidFileNameChars(), value) >= 0 ? '_' : value;
        }

        return Path.Combine(storageDirectory, new string(buffer) + ".json");
    }

    private static string NormalizeBoardId(string boardId)
    {
        string normalized = boardId?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("Board id is required.");
        }

        return normalized;
    }

    private static JsonObject ParseObject(string rawJson, string propertyName)
    {
        string normalized = string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson;
        JsonNode? node = JsonNode.Parse(normalized);
        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException($"{propertyName} must be a JSON object.");
        }

        return obj;
    }

    private static JsonNode? ParseLayout(string rawJson)
    {
        string normalized = string.IsNullOrWhiteSpace(rawJson) ? "null" : rawJson;
        return JsonNode.Parse(normalized);
    }

    private static JsonObject MergeBoard(string boardId, string? currentRawBoardJson, JsonObject ui, JsonObject metadata)
    {
        JsonObject board = JsonNode.Parse(currentRawBoardJson ?? string.Empty) as JsonObject ?? CreateDefaultBoard(boardId);
        board["id"] = boardId;
        board["ui"] = ui.DeepClone();
        board["metadata"] = metadata.DeepClone();
        return board;
    }

    private static LocalManagedBoardRecord CreateDefaultRecord(string boardId)
    {
        return new LocalManagedBoardRecord(boardId, CreateDefaultBoard(boardId), null);
    }

    private static JsonObject CreateDefaultBoard(string boardId)
    {
        return new JsonObject
        {
            ["id"] = boardId,
            ["ui"] = new JsonObject(),
            ["metadata"] = new JsonObject()
        };
    }

    private static JsonNode BuildLayoutNode(BoardCanvasLayoutState layoutState)
    {
        var cardIds = new JsonArray();
        foreach (string cardId in layoutState.CardIds)
        {
            cardIds.Add((JsonNode?)JsonValue.Create(cardId));
        }

        var positions = new JsonObject();
        foreach ((string cardId, BoardCanvasPointState position) in layoutState.Positions)
        {
            positions[cardId] = new JsonObject
            {
                ["x"] = JsonValue.Create(position.X),
                ["y"] = JsonValue.Create(position.Y),
            };
        }

        var widths = new JsonObject();
        foreach ((string cardId, double width) in layoutState.Widths)
        {
            widths[cardId] = JsonValue.Create(width);
        }

        JsonNode? viewport = layoutState.Viewport is null
            ? null
            : new JsonObject
            {
                ["x"] = JsonValue.Create(layoutState.Viewport.X),
                ["y"] = JsonValue.Create(layoutState.Viewport.Y),
                ["zoom"] = JsonValue.Create(layoutState.Viewport.Zoom),
            };

        return new JsonObject
        {
            ["canvas"] = new JsonObject
            {
                ["cardIds"] = cardIds,
                ["positions"] = positions,
                ["widths"] = widths,
                ["viewport"] = viewport,
            }
        };
    }

    private sealed record LocalManagedBoardRecord(string BoardId, JsonObject Board, JsonNode? Layout)
    {
        public ManagedBoardConfigState ToManagedState()
        {
            JsonObject ui = Board["ui"] as JsonObject ?? new JsonObject();
            JsonObject metadata = Board["metadata"] as JsonObject ?? new JsonObject();
            return new ManagedBoardConfigState(
                ui.ToJsonString(JsonOptions),
                metadata.ToJsonString(JsonOptions),
                Layout?.ToJsonString(JsonOptions) ?? "null",
                Board.ToJsonString(JsonOptions));
        }
    }
}