using System;
using System.Text.Json;

namespace DemoBoards_WinUI.SseReducer;

public sealed record BoardSseCanonicalEnvelope(
    string BoardId,
    string SummaryJson,
    string DataObjectsByTokenJson,
    string CardDefinitionsAndDataJson,
    string CardRuntimesByIdJson,
    string BoardCardComputedValuesJson,
    string CardChatViewsJson,
    string CardWatchPartiesJson,
    bool SummaryChanged,
    bool DataObjectsChanged,
    bool DefinitionsChanged,
    bool RuntimesChanged,
    bool ComputedValuesChanged,
    bool ChatsChanged,
    bool WatchpartiesChanged)
{
    public static BoardSseCanonicalEnvelope Parse(string? envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return Empty;
        }

        using JsonDocument document = JsonDocument.Parse(envelopeJson);
        JsonElement root = document.RootElement;
        JsonElement changes = TryGetObject(root, "changes");

        return new BoardSseCanonicalEnvelope(
            GetString(root, "boardId") ?? "unknown-board",
            TryGetObject(root, "summary").ValueKind is JsonValueKind.Undefined ? "null" : TryGetObject(root, "summary").GetRawText(),
            TryGetObject(root, "dataObjectsByToken").ValueKind is JsonValueKind.Undefined ? "{}" : TryGetObject(root, "dataObjectsByToken").GetRawText(),
            TryGetObject(root, "cardDefinitionsAndData").ValueKind is JsonValueKind.Undefined ? "{}" : TryGetObject(root, "cardDefinitionsAndData").GetRawText(),
            TryGetObject(root, "cardRuntimesById").ValueKind is JsonValueKind.Undefined ? "{}" : TryGetObject(root, "cardRuntimesById").GetRawText(),
            TryGetObject(root, "boardCardComputedValues").ValueKind is JsonValueKind.Undefined ? "{}" : TryGetObject(root, "boardCardComputedValues").GetRawText(),
            TryGetObject(root, "cardChatViews").ValueKind is JsonValueKind.Undefined ? "{}" : TryGetObject(root, "cardChatViews").GetRawText(),
            TryGetObject(root, "cardWatchParties").ValueKind is JsonValueKind.Undefined ? "{}" : TryGetObject(root, "cardWatchParties").GetRawText(),
            GetBool(changes, "summaryChanged", true),
            GetBool(changes, "dataObjectsChanged", true),
            GetBool(changes, "definitionsChanged", true),
            GetBool(changes, "runtimesChanged", true),
            GetBool(changes, "computedValuesChanged", true),
            GetBool(changes, "chatsChanged", true),
            GetBool(changes, "watchpartiesChanged", true));
    }

    public static BoardSseCanonicalEnvelope Empty { get; } = new(
        "unknown-board",
        "null",
        "{}",
        "{}",
        "{}",
        "{}",
        "{}",
        "{}",
        true,
        true,
        true,
        true,
        true,
        true,
        true);

    private static JsonElement TryGetObject(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out JsonElement value))
        {
            return value;
        }

        return default;
    }

    private static string? GetString(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static bool GetBool(JsonElement parent, string property, bool fallback)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return fallback;
    }
}