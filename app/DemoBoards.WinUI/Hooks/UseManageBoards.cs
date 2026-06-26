using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseManageBoards — Reactor port of useManageBoards.js
//
//  Drives the managed-boards admin surface (list/add/save/import/export). The web hook
//  POSTs each subcommand to a controlface origin; the embedded app hosts that same
//  manage-boards endpoint in-process, so every action routes through the generic
//  EmbeddedBoardClient.ManageBoardsAsync passthrough (the WinUI analog of postManageBoards).
//  State (boards / loading / error) lives in hook UseState, and the list auto-loads on
//  mount after a short delay, exactly like the web effect.
// =====================================================================================

/// <summary>A normalized managed-board list entry (port of <c>normalizeManagedBoardEntry</c>).</summary>
public sealed record ManagedBoardEntry(
    string Id,
    string Label,
    string Ai,
    string AiWorkspaceTemplate,
    string UiTemplate,
    string RefsTemplate,
    JsonObject Ui,
    JsonObject Metadata,
    JsonObject? RawBoard);

/// <summary>Options for <see cref="HookComponent{TProps}.UseManageBoards"/> (port of the web options bag).</summary>
public sealed record ManageBoardsOptions(bool Enabled = true, int ReloadDelayMs = 250);

/// <summary>Stable managed-boards action callbacks (port of <c>manageBoardsActions</c>).</summary>
public sealed record ManageBoardsActions(
    Func<Task<IReadOnlyList<ManagedBoardEntry>>> ListBoards,
    Func<JsonObject, Task<ManagedBoardEntry?>> AddBoard,
    Func<string, Task<ManagedBoardEntry?>> GetBoard,
    Func<string, JsonObject, Task<ManagedBoardEntry?>> SaveBoardMeta,
    Func<string, Task<JsonNode?>> GetLayout,
    Func<string, JsonObject, string?, Task<JsonNode?>> SaveLayout,
    Func<string, JsonObject, Task<ManagedBoardEntry?>> SaveBoardRecord,
    Func<string, Task<ManagedBoardEntry?>> RefreshBoard,
    Func<string, Task<JsonNode?>> ExportBoard,
    Func<string, JsonNode, string, Task<JsonNode?>> PreviewImportBoard,
    Func<string, JsonNode, string, bool, Task<JsonNode?>> ApplyImportBoard);

/// <summary>The object returned by <see cref="HookComponent{TProps}.UseManageBoards"/>.</summary>
public sealed record ManageBoards(
    IReadOnlyList<ManagedBoardEntry> ManagedBoards,
    bool LoadingManagedBoards,
    string ManageBoardsError,
    ManageBoardsActions ManageBoardsActions);

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useManageBoards</c>: managed-board list state plus the admin action callbacks.</summary>
    protected ManageBoards UseManageBoards(ManageBoardsOptions? options = null)
    {
        ManageBoardsOptions opts = options ?? new ManageBoardsOptions();
        EmbeddedBoardClient client = App.Current.BoardClient;

        var (boards, setBoards) = UseState<IReadOnlyList<ManagedBoardEntry>>(Array.Empty<ManagedBoardEntry>());
        var (loading, setLoading) = UseState(false);
        var (error, setError) = UseState(string.Empty);

        async Task<IReadOnlyList<ManagedBoardEntry>> ListBoards()
        {
            setLoading(true);
            setError(string.Empty);
            try
            {
                JsonNode? data = await client.ManageBoardsAsync("list-boards");
                IReadOnlyList<ManagedBoardEntry> next = NormalizeManagedBoardEntries(data);
                setBoards(next);
                return next;
            }
            catch (Exception nextError)
            {
                setBoards(Array.Empty<ManagedBoardEntry>());
                setError(nextError.Message);
                throw;
            }
            finally
            {
                setLoading(false);
            }
        }

        async Task<ManagedBoardEntry?> AddBoard(JsonObject candidate)
        {
            string boardId = ReadTrimmed(candidate, "boardId");
            string label = ReadTrimmed(candidate, "label");
            string pageTitle = ReadTrimmed(candidate, "pageTitle");
            string pageSubtitle = ReadTrimmed(candidate, "pageSubtitle");
            string ai = ReadTrimmed(candidate, "ai");
            string aiWorkspaceTemplate = ReadTrimmed(candidate, "aiWorkspaceTemplate");
            string uiTemplate = ReadTrimmed(candidate, "uiTemplate");
            string refsTemplate = ReadTrimmed(candidate, "refsTemplate");

            if (boardId.Length == 0) throw new InvalidOperationException("Board id is required");
            if (label.Length == 0) throw new InvalidOperationException("Label is required");
            if (pageTitle.Length == 0) throw new InvalidOperationException("Page title is required");
            if (pageSubtitle.Length == 0) throw new InvalidOperationException("Page subtitle is required");
            if (ai.Length == 0) throw new InvalidOperationException("AI is required");
            if (aiWorkspaceTemplate.Length == 0) throw new InvalidOperationException("AI workspace template is required");
            if (uiTemplate.Length == 0) throw new InvalidOperationException("UI template is required");
            if (refsTemplate.Length == 0) throw new InvalidOperationException("Refs template is required");

            var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (pageTitle.Length > 0) metadata["pageTitle"] = pageTitle;
            if (pageSubtitle.Length > 0) metadata["pageSubtitle"] = pageSubtitle;

            JsonNode? data = await client.ManageBoardsAsync("add-board", new
            {
                boardId,
                record = new
                {
                    label,
                    ai,
                    aiWorkspaceTemplate,
                    uiTemplate,
                    refsTemplate,
                    metadata,
                }
            });

            await ListBoards();
            return NormalizeManagedBoardEntry(GetChild(data, "board"));
        }

        async Task<ManagedBoardEntry?> SaveBoardMeta(string boardId, JsonObject metadata)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            JsonNode? data = await client.ManageBoardsAsync("save-meta", new
            {
                boardId = normalizedBoardId,
                metadata = metadata.DeepClone(),
            });
            return NormalizeManagedBoardEntry(GetChild(data, "board"));
        }

        async Task<JsonNode?> GetLayout(string boardId)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            JsonNode? data = await client.ManageBoardsAsync("get-layout", new { boardId = normalizedBoardId });
            return GetChild(data, "layout")?.DeepClone();
        }

        async Task<JsonNode?> SaveLayout(string boardId, JsonObject layout, string? mode)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            ArgumentNullException.ThrowIfNull(layout);
            string normalizedMode = mode?.Trim() ?? string.Empty;
            var args = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["boardId"] = normalizedBoardId,
                ["layout"] = layout.DeepClone(),
            };
            if (normalizedMode.Length > 0) args["mode"] = normalizedMode;
            JsonNode? data = await client.ManageBoardsAsync("save-layout", args);
            return GetChild(data, "layout")?.DeepClone();
        }

        async Task<ManagedBoardEntry?> SaveBoardRecord(string boardId, JsonObject record)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            ArgumentNullException.ThrowIfNull(record);
            JsonNode? data = await client.ManageBoardsAsync("save-board-record", new
            {
                boardId = normalizedBoardId,
                record = record.DeepClone(),
            });
            await ListBoards();
            return NormalizeManagedBoardEntry(GetChild(data, "board"));
        }

        async Task<JsonNode?> ExportBoard(string boardId)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            JsonNode? data = await client.ManageBoardsAsync("export-board", new { boardId = normalizedBoardId });
            return GetChild(data, "payload")?.DeepClone();
        }

        async Task<JsonNode?> PreviewImportBoard(string boardId, JsonNode payload, string mode)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            JsonNode? data = await client.ManageBoardsAsync("preview-import-board", new
            {
                boardId = normalizedBoardId,
                payload = payload?.DeepClone(),
                mode,
            });
            return GetChild(data, "preview")?.DeepClone();
        }

        async Task<JsonNode?> ApplyImportBoard(string boardId, JsonNode payload, string mode, bool applyBoardMetadata)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            JsonNode? data = await client.ManageBoardsAsync("apply-import-board", new
            {
                boardId = normalizedBoardId,
                payload = payload?.DeepClone(),
                mode,
                applyBoardMetadata,
            });
            return data?.DeepClone();
        }

        async Task<ManagedBoardEntry?> RefreshBoard(string boardId)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            JsonNode? data = await client.ManageBoardsAsync("refresh-board", new { boardId = normalizedBoardId });
            await ListBoards();
            return NormalizeManagedBoardEntry(GetChild(data, "board"));
        }

        async Task<ManagedBoardEntry?> GetBoard(string boardId)
        {
            string normalizedBoardId = NormalizeBoardId(boardId);
            JsonNode? data = await client.ManageBoardsAsync("get-board", new { boardId = normalizedBoardId });
            return NormalizeManagedBoardEntry(GetChild(data, "board"));
        }

        UseEffect(() =>
        {
            if (!opts.Enabled)
            {
                setBoards(Array.Empty<ManagedBoardEntry>());
                setLoading(false);
                setError(string.Empty);
                return () => { };
            }

            bool cancelled = false;

            async Task AutoLoad()
            {
                try
                {
                    await Task.Delay(Math.Max(0, opts.ReloadDelayMs));
                }
                catch
                {
                    // ignore delay faults
                }

                if (cancelled)
                {
                    return;
                }

                try
                {
                    await ListBoards();
                }
                catch
                {
                    // surfaced through error state
                }
            }

            _ = AutoLoad();
            return () => { cancelled = true; };
        }, opts.Enabled, opts.ReloadDelayMs);

        var actions = new ManageBoardsActions(
            ListBoards,
            AddBoard,
            GetBoard,
            SaveBoardMeta,
            GetLayout,
            SaveLayout,
            SaveBoardRecord,
            RefreshBoard,
            ExportBoard,
            PreviewImportBoard,
            ApplyImportBoard);

        return new ManageBoards(boards, loading, error, actions);
    }

    private static string NormalizeBoardId(string? boardId)
    {
        string normalized = boardId?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("Board id is required");
        }

        return normalized;
    }

    private static IReadOnlyList<ManagedBoardEntry> NormalizeManagedBoardEntries(JsonNode? data)
    {
        if (GetChild(data, "boards") is not JsonArray boards)
        {
            return Array.Empty<ManagedBoardEntry>();
        }

        var entries = new List<ManagedBoardEntry>();
        foreach (JsonNode? board in boards)
        {
            ManagedBoardEntry? entry = NormalizeManagedBoardEntry(board);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static ManagedBoardEntry? NormalizeManagedBoardEntry(JsonNode? board)
    {
        if (board is not JsonObject boardObject)
        {
            return null;
        }

        string id = ReadTrimmed(boardObject, "id");
        if (id.Length == 0)
        {
            return null;
        }

        string label = ReadTrimmed(boardObject, "label");
        return new ManagedBoardEntry(
            id,
            label.Length > 0 ? label : id,
            ReadTrimmed(boardObject, "ai"),
            ReadTrimmed(boardObject, "aiWorkspaceTemplate"),
            ReadTrimmed(boardObject, "uiTemplate"),
            ReadTrimmed(boardObject, "refsTemplate"),
            (boardObject["ui"] as JsonObject)?.DeepClone() as JsonObject ?? new JsonObject(),
            (boardObject["metadata"] as JsonObject)?.DeepClone() as JsonObject ?? new JsonObject(),
            boardObject.DeepClone() as JsonObject);
    }

    private static JsonNode? GetChild(JsonNode? node, string key) => (node as JsonObject)?[key];

    private static string ReadTrimmed(JsonObject source, string key)
    {
        return source.TryGetPropertyValue(key, out JsonNode? node)
            && node is JsonValue value
            && value.TryGetValue(out string? text)
            ? text.Trim()
            : string.Empty;
    }
}
