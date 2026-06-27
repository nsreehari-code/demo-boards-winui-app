using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseRuntimeCards — Reactor port of useRuntimeCards.js
//
//  Exposes the runtime-card authoring actions (list / upsert / remove) bound to the
//  controlplane MCP face on EmbeddedBoardClient. Mirrors the web unwrapPayload contract:
//  a success envelope yields its data (or null), a fail envelope throws, anything else is
//  returned verbatim.
// =====================================================================================

/// <summary>Runtime-card action callbacks (port of <c>useRuntimeCards</c>'s <c>runtimeCardActions</c>).</summary>
public sealed record RuntimeCardActions(
    Func<Task<JsonNode?>> ListRuntimeCards,
    Func<JsonNode, Task<JsonNode?>> UpsertRuntimeCard,
    Func<string, Task<JsonNode?>> RemoveRuntimeCard);

/// <summary>The object returned by <see cref="HookComponent{TProps}.UseRuntimeCards"/>.</summary>
public sealed record RuntimeCards(RuntimeCardActions RuntimeCardActions);

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useRuntimeCards</c>: the stable runtime-card action callbacks.</summary>
    protected RuntimeCards UseRuntimeCards(string boardId)
    {
        EmbeddedBoardClient client = App.Current.BoardClient;

        // useMemo parity: the frontend memoises runtimeCardActions on boardId so the callbacks
        // are stable across renders that don't change the board.
        return UseMemo<RuntimeCards>(
            () => new RuntimeCards(new RuntimeCardActions(
                ListRuntimeCards: async () => await client.ListRuntimeCardsAsync().ConfigureAwait(false),
                UpsertRuntimeCard: async candidate =>
                    UnwrapRuntimeCardPayload(await client.UpsertRuntimeCardAsync(candidate).ConfigureAwait(false), "upsertRuntimeCard"),
                RemoveRuntimeCard: async cardId =>
                    UnwrapRuntimeCardPayload(await client.RemoveRuntimeCardAsync(cardId).ConfigureAwait(false), "removeRuntimeCard"))),
            boardId);
    }

    /// <summary>Port of <c>useRuntimeCards</c>'s <c>unwrapPayload</c>: success → data, fail → throw, else → payload.</summary>
    internal static JsonNode? UnwrapRuntimeCardPayload(JsonNode? payload, string label)
    {
        if (payload is not JsonObject envelope)
        {
            return payload;
        }

        string status = envelope.TryGetPropertyValue("status", out JsonNode? statusNode)
            && statusNode is JsonValue statusValue
            && statusValue.TryGetValue(out string? statusString)
            ? statusString
            : string.Empty;

        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            JsonNode? data = envelope.TryGetPropertyValue("data", out JsonNode? dataNode) ? dataNode : null;
            return data?.DeepClone();
        }

        if (string.Equals(status, "fail", StringComparison.OrdinalIgnoreCase))
        {
            string error = envelope.TryGetPropertyValue("error", out JsonNode? errorNode)
                && errorNode is JsonValue errorValue
                && errorValue.TryGetValue(out string? errorString)
                ? errorString.Trim()
                : string.Empty;
            throw new InvalidOperationException(error.Length > 0 ? error : $"{label} failed");
        }

        return envelope.DeepClone();
    }
}
