using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DemoBoards.RuntimeHost;

public sealed record BoardCardField(string Key, string Value);

public sealed record BoardChatMessage(
    string Role,
    string Text,
    string Turn,
    bool Processing);

public sealed record BoardWatchpartyToolPayload(
    string Tool,
    string Action,
    string CardId,
    string TurnId,
    int? FileIndex);

public sealed record BoardWatchpartyState(
    string AgentOutput,
    string AgentTools,
    IReadOnlyList<BoardWatchpartyToolPayload> AgentToolPayloads);

public sealed record BoardRenderElement(
    string Kind,
    string Label,
    string ClassName,
    string Visible,
    string RawJson);

public sealed record BoardSourceDefinition(
    string BindTo,
    IReadOnlyList<BoardCardField> DetailFields);

public sealed record BoardCard(
    string Id,
    string Title,
    string Status,
    IReadOnlyDictionary<string, string> MetaValues,
    IReadOnlyList<BoardCardField> Fields,
    IReadOnlyList<BoardCardField> ComputedValues,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Provides,
    IReadOnlyList<string> ViewKinds,
    IReadOnlyList<BoardRenderElement> ViewElements,
    IReadOnlyList<BoardSourceDefinition> SourceDefinitions,
    IReadOnlyList<BoardChatMessage> ChatMessages,
    bool ChatReceiving,
    bool ChatProcessing,
    string RawDefinitionJson,
    string RawRuntimeJson,
    string SchemaVersion);

public sealed record BoardSnapshot(
    string BoardId,
    int CardCount,
    int Pending,
    int InProgress,
    int Failed,
    int Completed,
    IReadOnlyDictionary<string, string> DataObjectsByToken,
    IReadOnlyList<BoardCard> Cards)
{
    public static BoardSnapshot Empty { get; } = new(
        "unknown-board",
        0,
        0,
        0,
        0,
        0,
        new Dictionary<string, string>(),
        Array.Empty<BoardCard>());

    /// <summary>
    /// Parses the runtime's published payload (the same shape the reducer/producer
    /// emits: boardId, cardDefinitions, cardRuntimeById, statusSnapshot) into a
    /// typed snapshot the WinUI shell can render without touching JSON.
    /// </summary>
    public static BoardSnapshot Parse(string? publishedPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(publishedPayloadJson))
        {
            return Empty;
        }

        using JsonDocument document = JsonDocument.Parse(publishedPayloadJson);
        JsonElement root = document.RootElement;

        string boardId = GetString(root, "boardId") ?? "unknown-board";
        IReadOnlyDictionary<string, string> dataObjectsByToken = ParseDataObjects(root);

        JsonElement runtimeById = TryGetObject(root, "cardRuntimeById");
        JsonElement chatsById = TryGetObject(root, "cardChatsByCardId");
        var cards = new List<BoardCard>();

        if (root.TryGetProperty("cardDefinitions", out JsonElement definitions)
            && definitions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement definition in definitions.EnumerateArray())
            {
                cards.Add(ParseCard(definition, runtimeById, chatsById));
            }
        }

        (int pending, int inProgress, int failed, int completed) = ParseSummary(root);

        return new BoardSnapshot(
            boardId,
            cards.Count,
            pending,
            inProgress,
            failed,
            completed,
            dataObjectsByToken,
            cards);
    }

    public static IReadOnlyDictionary<string, BoardWatchpartyState> ParseWatchparties(string? publishedPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(publishedPayloadJson))
        {
            return new Dictionary<string, BoardWatchpartyState>(StringComparer.Ordinal);
        }

        using JsonDocument document = JsonDocument.Parse(publishedPayloadJson);
        JsonElement root = document.RootElement;
        JsonElement watchPartiesById = TryGetObject(root, "cardWatchParties");
        if (watchPartiesById.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, BoardWatchpartyState>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, BoardWatchpartyState>(StringComparer.Ordinal);
        foreach (JsonProperty property in watchPartiesById.EnumerateObject())
        {
            result[property.Name] = ParseWatchparty(watchPartiesById, property.Name);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> ParseDataObjects(JsonElement root)
    {
        if (!root.TryGetProperty("dataObjectsByToken", out JsonElement dataObjects)
            || dataObjects.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in dataObjects.EnumerateObject())
        {
            result[property.Name] = RenderValue(property.Value);
        }

        return result;
    }

    private static BoardCard ParseCard(JsonElement definition, JsonElement runtimeById, JsonElement chatsById)
    {
        string cardId = GetString(definition, "id") ?? "unknown-card";
        JsonElement cardData = TryGetObject(definition, "card_data");
        JsonElement meta = TryGetObject(definition, "meta");

        string title = GetString(cardData, "title") ?? GetString(meta, "title") ?? cardId;
        IReadOnlyDictionary<string, string> metaValues = ParseMetaValues(meta);

        var fields = new List<BoardCardField>();
        if (cardData.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in cardData.EnumerateObject())
            {
                if (property.NameEquals("title")
                    || property.NameEquals("requires")
                    || property.NameEquals("provides"))
                {
                    continue;
                }

                fields.Add(new BoardCardField(property.Name, RenderValue(property.Value)));
            }
        }

        IReadOnlyList<string> requires = ParseTokenList(cardData, "requires");
        IReadOnlyList<string> provides = ParseProvideList(cardData);
        IReadOnlyList<string> viewKinds = ParseViewKinds(definition);
        IReadOnlyList<BoardRenderElement> viewElements = ParseViewElements(definition);
        IReadOnlyList<BoardSourceDefinition> sourceDefinitions = ParseSourceDefinitions(definition);
        (IReadOnlyList<BoardChatMessage> chatMessages, bool chatReceiving, bool chatProcessing) = ParseChatMessages(chatsById, cardId);

        string status = "fresh";
        string schemaVersion = string.Empty;
        var computedValues = new List<BoardCardField>();
        string rawRuntimeJson = "{}";

        if (runtimeById.ValueKind == JsonValueKind.Object
            && runtimeById.TryGetProperty(cardId, out JsonElement runtime)
            && runtime.ValueKind == JsonValueKind.Object)
        {
            rawRuntimeJson = runtime.GetRawText();
            status = GetString(runtime, "status") ?? status;
            schemaVersion = GetString(runtime, "schema_version") ?? string.Empty;

            JsonElement computed = TryGetObject(runtime, "computed_values");
            if (computed.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in computed.EnumerateObject())
                {
                    computedValues.Add(new BoardCardField(property.Name, RenderValue(property.Value)));
                }
            }
        }

        return new BoardCard(
            cardId,
            title,
            status,
            metaValues,
            fields,
            computedValues,
            requires,
            provides,
            viewKinds,
            viewElements,
            sourceDefinitions,
            chatMessages,
            chatReceiving,
            chatProcessing,
            definition.GetRawText(),
            rawRuntimeJson,
            schemaVersion);
    }

    private static IReadOnlyDictionary<string, string> ParseMetaValues(JsonElement meta)
    {
        if (meta.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in meta.EnumerateObject())
        {
            result[property.Name] = RenderValue(property.Value);
        }

        return result;
    }

    private static (IReadOnlyList<BoardChatMessage> Messages, bool Receiving, bool Processing) ParseChatMessages(JsonElement chatsById, string cardId)
    {
        if (chatsById.ValueKind != JsonValueKind.Object
            || !chatsById.TryGetProperty(cardId, out JsonElement chatNode))
        {
            return (Array.Empty<BoardChatMessage>(), false, false);
        }

        JsonElement messages = chatNode;
        bool receiving = chatNode.ValueKind == JsonValueKind.Object
            && chatNode.TryGetProperty("receiving", out JsonElement receivingElement)
            && receivingElement.ValueKind == JsonValueKind.True;
        bool processing = chatNode.ValueKind == JsonValueKind.Object
            && chatNode.TryGetProperty("processing", out JsonElement processingElement)
            && processingElement.ValueKind == JsonValueKind.True;
        if (chatNode.ValueKind == JsonValueKind.Object
            && chatNode.TryGetProperty("messages", out JsonElement messageArray)
            && messageArray.ValueKind == JsonValueKind.Array)
        {
            messages = messageArray;
        }

        if (messages.ValueKind != JsonValueKind.Array)
        {
            return (Array.Empty<BoardChatMessage>(), receiving, processing);
        }

        var parsed = new List<BoardChatMessage>();
        foreach (JsonElement message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            parsed.Add(new BoardChatMessage(
                GetString(message, "role") ?? "system",
                GetString(message, "text") ?? string.Empty,
                GetString(message, "turn") ?? string.Empty,
                message.TryGetProperty("processing", out JsonElement messageProcessingElement) && messageProcessingElement.ValueKind == JsonValueKind.True));
        }

        return (parsed, receiving, processing);
    }

    private static BoardWatchpartyState ParseWatchparty(JsonElement watchPartiesById, string cardId)
    {
        if (watchPartiesById.ValueKind != JsonValueKind.Object
            || !watchPartiesById.TryGetProperty(cardId, out JsonElement cardWatchparty)
            || cardWatchparty.ValueKind != JsonValueKind.Object)
        {
            return EmptyWatchparty();
        }

        JsonElement agentOutputEvents = TryGetArray(cardWatchparty, "agent-output");
        JsonElement agentToolsEvents = TryGetArray(cardWatchparty, "agent-tools");

        string agentOutput = string.Empty;
        if (agentOutputEvents.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in agentOutputEvents.EnumerateArray())
            {
                string? nextText = GetNestedString(entry, "payload", "text");
                if (!string.IsNullOrWhiteSpace(nextText))
                {
                    agentOutput = nextText;
                }
            }
        }

        var toolPayloads = new List<BoardWatchpartyToolPayload>();
        var toolLines = new List<string>();
        if (agentToolsEvents.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in agentToolsEvents.EnumerateArray())
            {
                JsonElement payload = TryGetObject(entry, "payload");
                IReadOnlyList<BoardWatchpartyToolPayload> parsedPayloads = ParseWatchpartyToolPayloads(payload);
                foreach (BoardWatchpartyToolPayload parsedPayload in parsedPayloads)
                {
                    toolPayloads.Add(parsedPayload);
                    string formatted = FormatWatchpartyToolPayload(parsedPayload);
                    if (!string.IsNullOrWhiteSpace(formatted))
                    {
                        toolLines.Add(formatted);
                    }
                }

                if (parsedPayloads.Count == 0)
                {
                    string fallback = GetNestedString(entry, "payload", "text")?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        toolLines.Add(fallback);
                    }
                }
            }
        }

        return new BoardWatchpartyState(
            agentOutput,
            string.Join("\n", toolLines),
            toolPayloads);
    }

    private static (int Pending, int InProgress, int Failed, int Completed) ParseSummary(JsonElement root)
    {
        if (!root.TryGetProperty("statusSnapshot", out JsonElement statusSnapshot)
            || statusSnapshot.ValueKind != JsonValueKind.Object
            || !statusSnapshot.TryGetProperty("summary", out JsonElement summary)
            || summary.ValueKind != JsonValueKind.Object)
        {
            return (0, 0, 0, 0);
        }

        return (
            GetInt(summary, "pending"),
            GetInt(summary, "in_progress"),
            GetInt(summary, "failed"),
            GetInt(summary, "completed"));
    }

    private static IReadOnlyList<string> ParseTokenList(JsonElement cardData, string property)
    {
        if (cardData.ValueKind != JsonValueKind.Object
            || !cardData.TryGetProperty(property, out JsonElement value))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in value.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    AddToken(tokens, entry.GetString());
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in value.EnumerateObject())
            {
                AddToken(tokens, entry.Name);
            }
        }

        return tokens;
    }

    private static IReadOnlyList<string> ParseProvideList(JsonElement cardData)
    {
        if (cardData.ValueKind != JsonValueKind.Object
            || !cardData.TryGetProperty("provides", out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                AddToken(tokens, entry.GetString());
            }
            else if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("bindTo", out JsonElement bindTo)
                && bindTo.ValueKind == JsonValueKind.String)
            {
                AddToken(tokens, bindTo.GetString());
            }
        }

        return tokens;
    }

    private static IReadOnlyList<string> ParseViewKinds(JsonElement definition)
    {
        var kinds = new List<string>();
        foreach (BoardRenderElement element in ParseViewElements(definition))
        {
            AddToken(kinds, element.Kind);
        }

        return kinds;
    }

    private static IReadOnlyList<BoardRenderElement> ParseViewElements(JsonElement definition)
    {
        JsonElement view = TryGetObject(definition, "view");
        if (view.ValueKind != JsonValueKind.Object
            || !view.TryGetProperty("elements", out JsonElement elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BoardRenderElement>();
        }

        var parsed = new List<BoardRenderElement>();
        foreach (JsonElement element in elements.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("kind", out JsonElement kindElement)
                && kindElement.ValueKind == JsonValueKind.String)
            {
                parsed.Add(new BoardRenderElement(
                    kindElement.GetString() ?? string.Empty,
                    GetString(element, "label") ?? string.Empty,
                    GetString(element, "className") ?? string.Empty,
                    GetString(element, "visible") ?? string.Empty,
                    element.GetRawText()));
            }
        }

        return parsed;
    }

    private static IReadOnlyList<BoardSourceDefinition> ParseSourceDefinitions(JsonElement definition)
    {
        if (!definition.TryGetProperty("source_defs", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BoardSourceDefinition>();
        }

        var definitions = new List<BoardSourceDefinition>();
        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string bindTo = GetString(source, "bindTo") ?? string.Empty;
            var detailFields = new List<BoardCardField>();
            foreach (JsonProperty property in source.EnumerateObject())
            {
                if (property.NameEquals("bindTo") || property.Name.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                detailFields.Add(new BoardCardField(property.Name, RenderValue(property.Value)));
            }

            definitions.Add(new BoardSourceDefinition(bindTo, detailFields));
        }

        return definitions;
    }

    private static void AddToken(List<string> tokens, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token) && !tokens.Contains(token))
        {
            tokens.Add(token);
        }
    }

    private static IReadOnlyList<BoardWatchpartyToolPayload> ParseWatchpartyToolPayloads(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return ParseWatchpartyToolPayloads(value.GetString());
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var results = new List<BoardWatchpartyToolPayload>();
            foreach (JsonElement entry in value.EnumerateArray())
            {
                results.AddRange(ParseWatchpartyToolPayloads(entry));
            }

            return results;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<BoardWatchpartyToolPayload>();
        }

        if (!value.TryGetProperty("tool", out _)
            && GetString(value, "text") is string textValue)
        {
            return ParseWatchpartyToolPayloads(textValue);
        }

        BoardWatchpartyToolPayload? payload = BuildWatchpartyToolPayload(
            GetString(value, "tool"),
            GetString(value, "card_id") ?? GetString(value, "cardId"),
            GetString(value, "turn_id") ?? GetString(value, "turnId") ?? GetString(value, "turn"),
            TryGetInt(value, "file_idx") ?? TryGetInt(value, "fileIdx"),
            GetString(value, "action") ?? GetString(value, "action_enum") ?? GetString(value, "action_string"));

        return payload is null ? Array.Empty<BoardWatchpartyToolPayload>() : new[] { payload };
    }

    private static IReadOnlyList<BoardWatchpartyToolPayload> ParseWatchpartyToolPayloads(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<BoardWatchpartyToolPayload>();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(normalized);
            return ParseWatchpartyToolPayloads(document.RootElement);
        }
        catch
        {
            var parsed = new List<BoardWatchpartyToolPayload>();
            foreach (string line in normalized.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                BoardWatchpartyToolPayload? legacy = ParseLegacyWatchpartyToolLine(line);
                if (legacy is not null)
                {
                    parsed.Add(legacy);
                }
            }

            return parsed;
        }
    }

    private static BoardWatchpartyToolPayload? ParseLegacyWatchpartyToolLine(string? value)
    {
        string line = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        Match match = Regex.Match(line, "^(Invoking|Completed|Failed)\\s+'([^']+)'(?:\\s+(.*))?$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        string rawAction = match.Groups[1].Value;
        string rawToolName = match.Groups[2].Value;
        string rawDetails = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

        string normalizedAction = NormalizeWatchpartyToolAction(rawAction);
        string normalizedToolName = NormalizeLegacyToolName(rawToolName);
        if (string.IsNullOrWhiteSpace(normalizedAction) || string.IsNullOrWhiteSpace(normalizedToolName))
        {
            return null;
        }

        string cardId = Regex.Match(rawDetails, "\\bfor\\s+([A-Za-z0-9._-]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        Match fileMatch = Regex.Match(rawDetails, "\\bfile(?:\\s+no\\.)?\\s+(\\d+)\\b", RegexOptions.IgnoreCase);
        int? fileIndex = fileMatch.Success && int.TryParse(fileMatch.Groups[1].Value, out int parsedFileIndex)
            ? parsedFileIndex
            : null;

        return BuildWatchpartyToolPayload(normalizedToolName, cardId, string.Empty, fileIndex, normalizedAction);
    }

    private static BoardWatchpartyToolPayload? BuildWatchpartyToolPayload(string? tool, string? cardId, string? turnId, int? fileIndex, string? action)
    {
        string normalizedTool = NormalizePayloadToolName(tool);
        string normalizedAction = NormalizeWatchpartyToolAction(action);
        if (string.IsNullOrWhiteSpace(normalizedTool) || string.IsNullOrWhiteSpace(normalizedAction))
        {
            return null;
        }

        return new BoardWatchpartyToolPayload(
            normalizedTool,
            normalizedAction,
            NormalizeString(cardId),
            NormalizeString(turnId),
            fileIndex);
    }

    private static string FormatWatchpartyToolPayload(BoardWatchpartyToolPayload payload)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(payload.CardId))
        {
            parts.Add($"for {payload.CardId}");
        }

        if (payload.FileIndex is int fileIndex)
        {
            parts.Add($"file no. {fileIndex}");
        }

        string details = JoinPhrases(parts);
        return $"{HumanizeAction(payload.Action)} '{HumanizeToolName(payload.Tool)}'{details}";
    }

    private static string HumanizeToolName(string? tool)
    {
        string normalized = NormalizeString(tool);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Unknown MCP Tool";
        }

        return TitleCase(Regex.Replace(normalized, "^liveboards\\.", string.Empty, RegexOptions.IgnoreCase));
    }

    private static string HumanizeAction(string? action)
    {
        return NormalizeWatchpartyToolAction(action) switch
        {
            "invoking" => "Invoking",
            "completed" => "Completed",
            "failed" => "Failed",
            _ => TitleCase(action)
        };
    }

    private static string JoinPhrases(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        if (parts.Count == 1)
        {
            return $" {parts[0]}";
        }

        if (parts.Count == 2)
        {
            return $" {parts[0]} and {parts[1]}";
        }

        return $" {string.Join(", ", parts, 0, parts.Count - 1)} and {parts[^1]}";
    }

    private static string TitleCase(string? text)
    {
        string normalized = NormalizeString(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Unknown MCP Tool";
        }

        string[] words = Regex.Split(normalized, "[._\\-\\s]+", RegexOptions.None);
        var builder = new StringBuilder();
        foreach (string word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
            {
                builder.Append(word.AsSpan(1));
            }
        }

        return builder.Length == 0 ? "Unknown MCP Tool" : builder.ToString();
    }

    private static string NormalizePayloadToolName(string? value)
    {
        string normalized = NormalizeString(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.Contains('.', StringComparison.Ordinal)
            ? normalized
            : NormalizeLegacyToolName(normalized);
    }

    private static string NormalizeLegacyToolName(string? value)
    {
        string normalized = Regex.Replace(NormalizeString(value).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.Contains('.', StringComparison.Ordinal) ? normalized : $"liveboards.{normalized}";
    }

    private static string NormalizeWatchpartyToolAction(string? value)
    {
        string normalized = NormalizeString(value).ToLowerInvariant();
        return normalized is "invoking" or "completed" or "failed" ? normalized : string.Empty;
    }

    private static string NormalizeString(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static BoardWatchpartyState EmptyWatchparty()
    {
        return new BoardWatchpartyState(string.Empty, string.Empty, Array.Empty<BoardWatchpartyToolPayload>());
    }

    private static string RenderValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };
    }

    private static JsonElement TryGetObject(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value))
        {
            return value;
        }

        return default;
    }

    private static JsonElement TryGetArray(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array)
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

    private static string? GetNestedString(JsonElement parent, string property, string nestedProperty)
    {
        JsonElement nested = TryGetObject(parent, property);
        return GetString(nested, nestedProperty);
    }

    private static int? TryGetInt(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
        {
            return result;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out result))
        {
            return result;
        }

        return null;
    }

    private static int GetInt(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result))
        {
            return result;
        }

        return 0;
    }
}
