using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DemoBoards.RuntimeHost;

public sealed record BoardCardField(string Key, string Value);

public sealed record BoardChartPoint(string Label, double Value);

public sealed record BoardEdge(string From, string To, string Token);

public sealed record BoardCardElement(
    string Kind,
    string? Label,
    string? Text,
    IReadOnlyList<string> Items,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<BoardChartPoint> Points);

public sealed record BoardCard(
    string Id,
    string Title,
    string Status,
    IReadOnlyList<BoardCardField> Fields,
    IReadOnlyList<BoardCardField> ComputedValues,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Provides,
    string SchemaVersion,
    IReadOnlyList<BoardCardElement> Elements);

public sealed record BoardSnapshot(
    string BoardId,
    int CardCount,
    int Pending,
    int InProgress,
    int Failed,
    int Completed,
    IReadOnlyList<BoardCard> Cards,
    IReadOnlyList<BoardEdge> Edges)
{
    public static BoardSnapshot Empty { get; } = new(
        "unknown-board",
        0,
        0,
        0,
        0,
        0,
        Array.Empty<BoardCard>(),
        Array.Empty<BoardEdge>());

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

        JsonElement runtimeById = TryGetObject(root, "cardRuntimeById");
        var cards = new List<BoardCard>();

        if (root.TryGetProperty("cardDefinitions", out JsonElement definitions)
            && definitions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement definition in definitions.EnumerateArray())
            {
                cards.Add(ParseCard(definition, runtimeById));
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
            cards,
            BuildEdges(cards));
    }

    private static IReadOnlyList<BoardEdge> BuildEdges(IReadOnlyList<BoardCard> cards)
    {
        var providerByToken = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (BoardCard card in cards)
        {
            foreach (string token in card.Provides)
            {
                if (!providerByToken.ContainsKey(token))
                {
                    providerByToken[token] = card.Id;
                }
            }
        }

        var edges = new List<BoardEdge>();
        foreach (BoardCard card in cards)
        {
            foreach (string token in card.Requires)
            {
                if (providerByToken.TryGetValue(token, out string? provider)
                    && !string.Equals(provider, card.Id, StringComparison.Ordinal))
                {
                    edges.Add(new BoardEdge(provider, card.Id, token));
                }
            }
        }

        return edges;
    }

    private static BoardCard ParseCard(JsonElement definition, JsonElement runtimeById)
    {
        string cardId = GetString(definition, "id") ?? "unknown-card";
        JsonElement cardData = TryGetObject(definition, "card_data");

        string title = GetString(cardData, "title") ?? cardId;

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

        string status = "fresh";
        string schemaVersion = string.Empty;
        var computedValues = new List<BoardCardField>();
        JsonElement computed = default;

        if (runtimeById.ValueKind == JsonValueKind.Object
            && runtimeById.TryGetProperty(cardId, out JsonElement runtime)
            && runtime.ValueKind == JsonValueKind.Object)
        {
            status = GetString(runtime, "status") ?? status;
            schemaVersion = GetString(runtime, "schema_version") ?? string.Empty;

            computed = TryGetObject(runtime, "computed_values");
            if (computed.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in computed.EnumerateObject())
                {
                    computedValues.Add(new BoardCardField(property.Name, RenderValue(property.Value)));
                }
            }
        }

        IReadOnlyList<BoardCardElement> elements = ParseElements(definition, cardData, computed);

        return new BoardCard(
            cardId,
            title,
            status,
            fields,
            computedValues,
            requires,
            provides,
            schemaVersion,
            elements);
    }

    private static IReadOnlyList<BoardCardElement> ParseElements(
        JsonElement definition,
        JsonElement cardData,
        JsonElement computed)
    {
        JsonElement view = TryGetObject(definition, "view");
        if (view.ValueKind != JsonValueKind.Object
            || !view.TryGetProperty("elements", out JsonElement elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BoardCardElement>();
        }

        var namespaces = new Dictionary<string, JsonElement>
        {
            ["card"] = definition,
            ["card_data"] = cardData,
            ["computed_values"] = computed
        };

        var parsed = new List<BoardCardElement>();
        foreach (JsonElement element in elements.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                parsed.Add(ParseElement(element, namespaces));
            }
        }

        return parsed;
    }

    private static BoardCardElement ParseElement(
        JsonElement element,
        IReadOnlyDictionary<string, JsonElement> namespaces)
    {
        string kind = GetString(element, "kind") ?? "narrative";
        string? label = GetString(element, "label");
        JsonElement data = TryGetObject(element, "data");

        JsonElement? resolved = ResolveBind(GetString(data, "bind"), namespaces);
        if (resolved is null && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("text", out JsonElement textLiteral))
            {
                resolved = textLiteral;
            }
            else if (data.TryGetProperty("value", out JsonElement valueLiteral))
            {
                resolved = valueLiteral;
            }
        }

        if (kind == "ref")
        {
            kind = GetString(data, "fallbackKind") ?? InferKind(resolved);
        }

        switch (kind)
        {
            case "list":
                return new BoardCardElement(kind, label, null, ResolveItems(resolved), Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), Array.Empty<BoardChartPoint>());
            case "table":
            case "editable-table":
                (IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows) = ResolveTable(resolved);
                return new BoardCardElement(kind, label, null, Array.Empty<string>(), columns, rows, Array.Empty<BoardChartPoint>());
            case "chart":
                return new BoardCardElement(kind, label, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), ResolveChartPoints(resolved));
            default:
                return new BoardCardElement(kind, label, ResolveText(resolved), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), Array.Empty<BoardChartPoint>());
        }
    }

    private static IReadOnlyList<BoardChartPoint> ResolveChartPoints(JsonElement? resolved)
    {
        if (resolved is not JsonElement value || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BoardChartPoint>();
        }

        var points = new List<BoardChartPoint>();
        int autoIndex = 0;
        foreach (JsonElement entry in value.EnumerateArray())
        {
            autoIndex++;
            if (entry.ValueKind == JsonValueKind.Number && entry.TryGetDouble(out double scalar))
            {
                points.Add(new BoardChartPoint(autoIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), scalar));
            }
            else if (entry.ValueKind == JsonValueKind.Object)
            {
                string pointLabel = GetString(entry, "label")
                    ?? GetString(entry, "name")
                    ?? GetString(entry, "x")
                    ?? autoIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                double pointValue = ReadChartValue(entry);
                points.Add(new BoardChartPoint(pointLabel, pointValue));
            }
        }

        return points;
    }

    private static double ReadChartValue(JsonElement entry)
    {
        foreach (string key in new[] { "value", "y", "count", "amount" })
        {
            if (entry.TryGetProperty(key, out JsonElement candidate)
                && candidate.ValueKind == JsonValueKind.Number
                && candidate.TryGetDouble(out double number))
            {
                return number;
            }
        }

        return 0;
    }

    private static string InferKind(JsonElement? resolved)
    {
        if (resolved is JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array) return "table";
            if (value.ValueKind == JsonValueKind.String) return "text";
        }

        return "narrative";
    }

    private static string ResolveText(JsonElement? resolved)
    {
        return resolved is JsonElement value ? RenderValue(value) : string.Empty;
    }

    private static IReadOnlyList<string> ResolveItems(JsonElement? resolved)
    {
        if (resolved is not JsonElement value || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var items = new List<string>();
        foreach (JsonElement entry in value.EnumerateArray())
        {
            items.Add(RenderValue(entry));
        }

        return items;
    }

    private static (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) ResolveTable(JsonElement? resolved)
    {
        if (resolved is not JsonElement value || value.ValueKind != JsonValueKind.Array)
        {
            return (Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());
        }

        var columns = new List<string>();
        var rawRows = new List<JsonElement>();
        foreach (JsonElement row in value.EnumerateArray())
        {
            rawRows.Add(row);
            if (row.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in row.EnumerateObject())
                {
                    if (!columns.Contains(property.Name))
                    {
                        columns.Add(property.Name);
                    }
                }
            }
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (JsonElement row in rawRows)
        {
            var cells = new List<string>();
            if (row.ValueKind == JsonValueKind.Object)
            {
                foreach (string column in columns)
                {
                    cells.Add(row.TryGetProperty(column, out JsonElement cell) ? RenderValue(cell) : string.Empty);
                }
            }
            else
            {
                cells.Add(RenderValue(row));
            }

            rows.Add(cells);
        }

        return (columns, rows);
    }

    private static JsonElement? ResolveBind(string? bind, IReadOnlyDictionary<string, JsonElement> namespaces)
    {
        if (string.IsNullOrWhiteSpace(bind))
        {
            return null;
        }

        IReadOnlyList<string> parts = PathParts(bind);
        if (parts.Count == 0 || !namespaces.TryGetValue(parts[0], out JsonElement current))
        {
            return null;
        }

        for (int index = 1; index < parts.Count; index++)
        {
            if (!TryStep(current, parts[index], out current))
            {
                return null;
            }
        }

        return current;
    }

    private static bool TryStep(JsonElement current, string part, out JsonElement next)
    {
        if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out next))
        {
            return true;
        }

        if (current.ValueKind == JsonValueKind.Array
            && int.TryParse(part, out int arrayIndex)
            && arrayIndex >= 0
            && arrayIndex < current.GetArrayLength())
        {
            next = current[arrayIndex];
            return true;
        }

        next = default;
        return false;
    }

    private static IReadOnlyList<string> PathParts(string path)
    {
        string normalized = System.Text.RegularExpressions.Regex.Replace(path, "\\[(\\d+)\\]", ".$1");
        var parts = new List<string>();
        foreach (string segment in normalized.Split('.'))
        {
            if (!string.IsNullOrEmpty(segment))
            {
                parts.Add(segment);
            }
        }

        return parts;
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

    private static void AddToken(List<string> tokens, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token) && !tokens.Contains(token))
        {
            tokens.Add(token);
        }
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
