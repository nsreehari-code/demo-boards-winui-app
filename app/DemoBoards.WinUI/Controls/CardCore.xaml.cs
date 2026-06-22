using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace DemoBoards_WinUI.Controls;

public sealed partial class CardCore : UserControl
{
    public CardCore()
    {
        InitializeComponent();
    }

    public void Render(BoardStore store, BoardCard card)
    {
        LayoutHost.Children.Clear();
        if (card.ViewElements.Count == 0)
        {
            return;
        }

        using JsonDocument definition = JsonDocument.Parse(card.RawDefinitionJson);
        using JsonDocument runtime = JsonDocument.Parse(card.RawRuntimeJson);

        var namespaces = BuildNamespaces(store, card, definition.RootElement, runtime.RootElement);

        foreach (BoardRenderElement element in card.ViewElements)
        {
            if (!IsVisible(namespaces, element.Visible))
            {
                continue;
            }

            (string kind, string rawRenderDefJson, JsonElement data) = NormalizeElement(namespaces, element);
            var view = new CardCoreView();
            view.Render(kind, element.Label, data, rawRenderDefJson, (value, request) => HandleSaveAsync(card, value, request));
            LayoutHost.Children.Add(view);
        }
    }

    private static async Task HandleSaveAsync(BoardCard card, object? value, CardCoreView.SaveRequest request)
    {
        EmbeddedBoardClient client = ((App)Application.Current).BoardClient;

        if (string.Equals(request.Kind, "actions", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(request.ButtonId))
        {
            await client.DispatchActionAsync(card.Id, "action", new { buttonId = request.ButtonId, elemId = request.ElemId }).ConfigureAwait(false);
            return;
        }

        string? writeTo = request.WriteTo;
        if (string.IsNullOrWhiteSpace(writeTo) && string.Equals(request.Kind, "notes", StringComparison.OrdinalIgnoreCase))
        {
            writeTo = "card_data.notes";
        }

        object patch = BuildPatch(writeTo, value);
        await client.PatchCardAsync(card.Id, patch).ConfigureAwait(false);
    }

    private static object BuildPatch(string? writeTo, object? value)
    {
        if (string.Equals(writeTo, "card_data", StringComparison.Ordinal))
        {
            return new Dictionary<string, object?> { ["card_data"] = value ?? new Dictionary<string, object?>() };
        }

        if (!string.IsNullOrWhiteSpace(writeTo) && writeTo.StartsWith("card_data.", StringComparison.Ordinal))
        {
            string[] parts = PathParts(writeTo["card_data.".Length..]);
            return new Dictionary<string, object?> { ["card_data"] = BuildNested(parts, value) };
        }

        if (value is IDictionary<string, object?> objectDictionary)
        {
            return new Dictionary<string, object?> { ["card_data"] = objectDictionary };
        }

        return new Dictionary<string, object?> { ["card_data"] = value };
    }

    private static object? BuildNested(string[] parts, object? value)
    {
        if (parts.Length == 0)
        {
            return value;
        }

        var root = new Dictionary<string, object?>();
        IDictionary<string, object?> current = root;
        for (int index = 0; index < parts.Length - 1; index += 1)
        {
            var next = new Dictionary<string, object?>();
            current[parts[index]] = next;
            current = next;
        }

        current[parts[^1]] = value;
        return root;
    }

    private static Dictionary<string, JsonElement> BuildNamespaces(BoardStore store, BoardCard card, JsonElement definition, JsonElement runtime)
    {
        var namespaces = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["card"] = ParseClone(card.RawDefinitionJson),
            ["card_data"] = definition.TryGetProperty("card_data", out JsonElement cardData) ? Clone(cardData) : ParseClone("{}"),
            ["computed_values"] = runtime.TryGetProperty("computed_values", out JsonElement computedValues) ? Clone(computedValues) : ParseClone("{}"),
            ["runtime_state"] = runtime.TryGetProperty("runtime", out JsonElement runtimeState) ? Clone(runtimeState) : ParseClone("{}"),
            ["requires"] = ParseClone(BuildRequiresJson(store, card))
        };

        return namespaces;
    }

    private static string BuildRequiresJson(BoardStore store, BoardCard card)
    {
        return JsonSerializer.Serialize(store.GetRequiredDataObjects(card));
    }

    private static bool IsVisible(IReadOnlyDictionary<string, JsonElement> namespaces, string visibleBind)
    {
        if (string.IsNullOrWhiteSpace(visibleBind)) return true;
        return TryResolveBind(namespaces, visibleBind, out JsonElement resolved)
            && resolved.ValueKind != JsonValueKind.False
            && resolved.ValueKind != JsonValueKind.Null
            && resolved.ValueKind != JsonValueKind.Undefined;
    }

    private static (string Kind, string RawRenderDefJson, JsonElement Data) NormalizeElement(IReadOnlyDictionary<string, JsonElement> namespaces, BoardRenderElement element)
    {
        using JsonDocument document = JsonDocument.Parse(element.RawJson);
        JsonElement root = document.RootElement;
        string decoratedRenderDefJson = DecorateRenderDefinition(namespaces, element.RawJson);
        JsonElement data = root.TryGetProperty("data", out JsonElement rootData) ? rootData : default;
        string kind = root.TryGetProperty("kind", out JsonElement kindElement) && kindElement.ValueKind == JsonValueKind.String
            ? kindElement.GetString() ?? string.Empty
            : element.Kind;

        JsonElement baseData = default;
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("bind", out JsonElement bindElement)
            && bindElement.ValueKind == JsonValueKind.String)
        {
            TryResolveBind(namespaces, bindElement.GetString() ?? string.Empty, out baseData);
        }

        if (!string.Equals(kind, "ref", StringComparison.OrdinalIgnoreCase))
        {
            return (kind, decoratedRenderDefJson, baseData);
        }

        JsonElement viewRaw = default;
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("viewBind", out JsonElement viewBindElement)
            && viewBindElement.ValueKind == JsonValueKind.String)
        {
            TryResolveBind(namespaces, viewBindElement.GetString() ?? string.Empty, out viewRaw);
        }

        string resolvedKind = ResolveRefKind(data, viewRaw, baseData);
        return (resolvedKind, decoratedRenderDefJson, viewRaw.ValueKind != JsonValueKind.Undefined ? viewRaw : baseData);
    }

    private static string DecorateRenderDefinition(IReadOnlyDictionary<string, JsonElement> namespaces, string rawRenderDefJson)
    {
        JsonNode? parsedNode = JsonNode.Parse(rawRenderDefJson);
        if (parsedNode is not JsonObject root)
        {
            return rawRenderDefJson;
        }

        if (root["data"] is not JsonObject dataObject
            || dataObject["writeTo"] is not JsonValue writeToValue
            || !writeToValue.TryGetValue<string>(out string? writeTo)
            || string.IsNullOrWhiteSpace(writeTo)
            || !TryResolveBind(namespaces, writeTo, out JsonElement resolvedWriteValue))
        {
            return rawRenderDefJson;
        }

        root["resolvedWriteValue"] = JsonNode.Parse(resolvedWriteValue.GetRawText());
        return root.ToJsonString();
    }

    private static string ResolveRefKind(JsonElement data, JsonElement viewRaw, JsonElement initialData)
    {
        if (viewRaw.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(viewRaw.GetString()))
        {
            return viewRaw.GetString() ?? "text";
        }

        if (viewRaw.ValueKind == JsonValueKind.Object
            && viewRaw.TryGetProperty("kind", out JsonElement kindElement)
            && kindElement.ValueKind == JsonValueKind.String)
        {
            return kindElement.GetString() ?? "text";
        }

        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("fallbackKind", out JsonElement fallbackKind)
            && fallbackKind.ValueKind == JsonValueKind.String)
        {
            return fallbackKind.GetString() ?? "text";
        }

        if (initialData.ValueKind == JsonValueKind.Array) return "table";
        if (initialData.ValueKind == JsonValueKind.String) return "text";
        return "narrative";
    }

    private static bool TryResolveBind(IReadOnlyDictionary<string, JsonElement> namespaces, string bind, out JsonElement resolved)
    {
        resolved = default;
        string[] parts = PathParts(bind);
        if (parts.Length == 0) return false;
        if (!namespaces.TryGetValue(parts[0], out JsonElement current)) return false;

        for (int index = 1; index < parts.Length; index += 1)
        {
            string part = parts[index];
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(part, out current)) return false;
                continue;
            }

            if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out int itemIndex))
            {
                JsonElement[] items = current.EnumerateArray().ToArray();
                if (itemIndex < 0 || itemIndex >= items.Length) return false;
                current = items[itemIndex];
                continue;
            }

            return false;
        }

        resolved = Clone(current);
        return true;
    }

    private static string[] PathParts(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Array.Empty<string>();
        return path.Replace("[", ".").Replace("]", string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
    }

    private static JsonElement ParseClone(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement Clone(JsonElement element)
    {
        return JsonDocument.Parse(element.GetRawText()).RootElement.Clone();
    }
}
