using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  ChatMessages — Reactor port of demo-boards-frontend/src/lib/chatMessages.js
//
//  Pure helpers for the chat conversation hook: accumulating live SSE-style snapshots
//  (which may only carry the latest turn) into a stable, append-only list, merging
//  backward-paginated history pages, and the small turn-id utilities the hook keys its
//  history/live boundary on. Messages are the same plain prop-bags the shared chat
//  components consume (keys: role, text, turn, processing).
// =====================================================================================

internal readonly record struct LiveChatEntry(string Key, IReadOnlyDictionary<string, object?> Msg);

internal static class ChatMessages
{
    /// <summary>
    /// Merge a fresh snapshot into the accumulated live messages. New messages append; an existing
    /// message (same turn/role/occurrence) is updated in place to support streaming text. Returns the
    /// previous list unchanged when nothing changed.
    /// </summary>
    public static IReadOnlyList<LiveChatEntry> MergeLiveMessages(
        IReadOnlyList<LiveChatEntry> prev,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> incoming)
    {
        if (incoming.Count == 0)
        {
            return prev;
        }

        var byKey = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (LiveChatEntry entry in prev)
        {
            byKey[entry.Key] = entry.Msg;
            order.Add(entry.Key);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool changed = false;
        foreach (IReadOnlyDictionary<string, object?> msg in incoming)
        {
            string turn = BoardData.Str(msg, "turn") ?? string.Empty;
            string role = BoardData.Str(msg, "role") ?? string.Empty;
            string baseKey = $"{turn}|{role}";
            int occurrence = counts.TryGetValue(baseKey, out int count) ? count : 0;
            counts[baseKey] = occurrence + 1;
            string key = $"{baseKey}|{occurrence}";

            if (!byKey.ContainsKey(key))
            {
                order.Add(key);
                changed = true;
            }
            else if (!ReferenceEquals(byKey[key], msg))
            {
                changed = true;
            }

            byKey[key] = msg;
        }

        if (!changed)
        {
            return prev;
        }

        return order.Select(key => new LiveChatEntry(key, byKey[key])).ToList();
    }

    /// <summary>Merge two ordered message arrays (existing before incoming) into a flat de-duplicated list.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> MergeMessageArrays(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> existingMessages,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> incomingMessages)
    {
        IReadOnlyList<LiveChatEntry> seeded = MergeLiveMessages(Array.Empty<LiveChatEntry>(), existingMessages);
        return MergeLiveMessages(seeded, incomingMessages).Select(entry => entry.Msg).ToList();
    }

    public static string GetMessageTurnId(IReadOnlyDictionary<string, object?>? msg) =>
        msg is null ? string.Empty : (BoardData.Str(msg, "turn") ?? string.Empty).Trim();

    public static string GetFirstTurnId(IReadOnlyList<IReadOnlyDictionary<string, object?>> messages)
    {
        foreach (IReadOnlyDictionary<string, object?> msg in messages)
        {
            string turnId = GetMessageTurnId(msg);
            if (!string.IsNullOrEmpty(turnId))
            {
                return turnId;
            }
        }

        return string.Empty;
    }

    public static int CountDistinctTurns(IReadOnlyList<IReadOnlyDictionary<string, object?>> messages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, object?> msg in messages)
        {
            string turnId = GetMessageTurnId(msg);
            if (!string.IsNullOrEmpty(turnId))
            {
                seen.Add(turnId);
            }
        }

        return seen.Count;
    }

    public static string MakeTurnId() =>
        Guid.NewGuid().ToString("N")[..6];

    /// <summary>A scalar signature for a message list, suitable as a Reactor effect dependency.</summary>
    public static string SignatureOf(IReadOnlyList<IReadOnlyDictionary<string, object?>> messages)
    {
        if (messages.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "\u0001",
            messages.Select(msg =>
                $"{BoardData.Str(msg, "turn")}|{BoardData.Str(msg, "role")}|{BoardData.Str(msg, "text")}|{BoardData.Bool(msg, "processing")}"));
    }

    /// <summary>
    /// Parse the JSON payload returned by the <c>inspect.chat-messages-on-cards</c> MCP tool into the
    /// shared chat-message prop-bags. Mirrors the unwrap in <c>fetchChatMessagesBeforeTurn</c>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParseChatMessagesPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            JsonElement data = root;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("status", out JsonElement status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "success", StringComparison.Ordinal)
                && root.TryGetProperty("data", out JsonElement dataElement))
            {
                data = dataElement;
            }

            if (data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("messages", out JsonElement messages)
                || messages.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<IReadOnlyDictionary<string, object?>>();
            }

            var result = new List<IReadOnlyDictionary<string, object?>>(messages.GetArrayLength());
            foreach (JsonElement message in messages.EnumerateArray())
            {
                if (message.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["role"] = ReadString(message, "role"),
                    ["text"] = ReadString(message, "text"),
                    ["turn"] = ReadString(message, "turn"),
                    ["processing"] = message.TryGetProperty("processing", out JsonElement processing)
                        && processing.ValueKind == JsonValueKind.True,
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
