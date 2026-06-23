using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorCardFrontContentProps(BoardCard Card);

public sealed class ReactorCardFrontContentComponent : Component<ReactorCardFrontContentProps>
{
    public override Element Render()
    {
        BoardCard card = Props.Card;
        if (card.ViewElements.Count == 0)
        {
            return card.Fields.Count == 0
                ? TextBlock("No content.").Opacity(0.62)
                : BuildFieldList(card.Fields);
        }

        using JsonDocument definition = JsonDocument.Parse(card.RawDefinitionJson);
        using JsonDocument runtime = JsonDocument.Parse(card.RawRuntimeJson);
        IReadOnlyDictionary<string, JsonElement> namespaces = BuildNamespaces(App.Current.BoardStore, card, definition.RootElement, runtime.RootElement);

        var elements = new List<Element>();
        foreach (BoardRenderElement element in card.ViewElements)
        {
            if (!IsVisible(namespaces, element.Visible))
            {
                continue;
            }

            (string kind, string rawRenderDefJson, JsonElement data) = NormalizeElement(namespaces, element);
            string rawDataJson = data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "null" : data.GetRawText();

            elements.Add(Component<ReactorCardViewElementComponent, ReactorCardViewElementProps>(
                new ReactorCardViewElementProps(card, kind, element.Label, rawDataJson, rawRenderDefJson)));
        }

        return elements.Count == 0
            ? TextBlock("No visible content.").Opacity(0.62)
            : VStack(10, elements.ToArray());
    }

    private static IReadOnlyDictionary<string, JsonElement> BuildNamespaces(BoardStore store, BoardCard card, JsonElement definition, JsonElement runtime)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["card"] = ParseClone(card.RawDefinitionJson),
            ["card_data"] = definition.TryGetProperty("card_data", out JsonElement cardData) ? Clone(cardData) : ParseClone("{}"),
            ["computed_values"] = runtime.TryGetProperty("computed_values", out JsonElement computedValues) ? Clone(computedValues) : ParseClone("{}"),
            ["runtime_state"] = runtime.TryGetProperty("runtime", out JsonElement runtimeState) ? Clone(runtimeState) : ParseClone("{}"),
            ["requires"] = ParseClone(JsonSerializer.Serialize(store.GetRequiredDataObjects(card)))
        };
    }

    private static bool IsVisible(IReadOnlyDictionary<string, JsonElement> namespaces, string visibleBind)
    {
        if (string.IsNullOrWhiteSpace(visibleBind))
        {
            return true;
        }

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
        using JsonDocument source = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = source.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return rawRenderDefJson;
        }

        using JsonDocument mutable = JsonDocument.Parse(rawRenderDefJson);
        JsonElement mutableRoot = mutable.RootElement;
        if (!mutableRoot.TryGetProperty("data", out JsonElement dataObject)
            || dataObject.ValueKind != JsonValueKind.Object
            || !dataObject.TryGetProperty("writeTo", out JsonElement writeToElement)
            || writeToElement.ValueKind != JsonValueKind.String
            || !TryResolveBind(namespaces, writeToElement.GetString() ?? string.Empty, out JsonElement resolvedWriteValue))
        {
            return rawRenderDefJson;
        }

        using JsonDocument wrapper = JsonDocument.Parse("{}");
        var node = JsonSerializer.Deserialize<Dictionary<string, object?>>(rawRenderDefJson) ?? new Dictionary<string, object?>();
        node["resolvedWriteValue"] = JsonSerializer.Deserialize<object?>(resolvedWriteValue.GetRawText());
        return JsonSerializer.Serialize(node);
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
        if (parts.Length == 0 || !namespaces.TryGetValue(parts[0], out JsonElement current))
        {
            return false;
        }

        for (int index = 1; index < parts.Length; index += 1)
        {
            string part = parts[index];
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(part, out current))
                {
                    return false;
                }

                continue;
            }

            if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out int itemIndex))
            {
                JsonElement[] items = current.EnumerateArray().ToArray();
                if (itemIndex < 0 || itemIndex >= items.Length)
                {
                    return false;
                }

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
        return string.IsNullOrWhiteSpace(path)
            ? Array.Empty<string>()
            : path.Replace("[", ".").Replace("]", string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
    }

    private static JsonElement ParseClone(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement Clone(JsonElement element)
    {
        return JsonDocument.Parse(element.GetRawText()).RootElement.Clone();
    }

    private static Element BuildFieldList(IReadOnlyList<BoardCardField> fields)
    {
        return VStack(2, fields.Select(field =>
            (Element)HStack(6,
                TextBlock($"{field.Key}:").Opacity(0.8),
                TextBlock(field.Value).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords))
        ).ToArray());
    }
}

public sealed record ReactorCardViewElementProps(BoardCard Card, string Kind, string Label, string RawDataJson, string RawRenderDefJson);

public sealed class ReactorCardViewElementComponent : Component<ReactorCardViewElementProps>
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    public sealed record SaveRequest(string Kind, string? WriteTo, string? ButtonId = null, string? ElemId = null);
    private sealed record ChoiceOption(string Value, string Label);
    private sealed record FieldConfig(string Key, string? Title, string? Type, string? Placeholder, IReadOnlyList<ChoiceOption> Options, string? ActionLabel);
    private sealed record SingleFieldConfig(string? WriteTo, string FieldKey, object? CurrentValue, FieldConfig Field);

    public override Element Render()
    {
        JsonElement data = ParseElement(Props.RawDataJson);
        string effectiveKind = NormalizeLegacyKind(Props.Kind, Props.RawRenderDefJson, data);
        string initialDraft = CreateInitialDraft(effectiveKind, data);

        var (draftText, setDraftText) = UseState(initialDraft);
        var (statusText, setStatusText) = UseState(string.Empty);
        var (todoComposerText, setTodoComposerText) = UseState(string.Empty);
        var (saving, setSaving) = UseState(false);

        UseEffect(() =>
        {
            setDraftText(CreateInitialDraft(effectiveKind, ParseElement(Props.RawDataJson)));
            setTodoComposerText(string.Empty);
            setStatusText(string.Empty);
        }, Props.RawDataJson, Props.RawRenderDefJson, effectiveKind);

        var sections = new List<Element>();
        if (!string.IsNullOrWhiteSpace(Props.Label) && effectiveKind is not "metric" and not "alert")
        {
            sections.Add(TextBlock(Props.Label).Opacity(0.72).FontSize(12));
        }

        sections.Add(BuildBody(effectiveKind, data, draftText, setDraftText, todoComposerText, setTodoComposerText, statusText, setStatusText, saving, setSaving, initialDraft));

        return VStack(6, sections.ToArray());
    }

    private Element BuildBody(
        string kind,
        JsonElement data,
        string draftText,
        Action<string> setDraftText,
        string todoComposerText,
        Action<string> setTodoComposerText,
        string statusText,
        Action<string> setStatusText,
        bool saving,
        Action<bool> setSaving,
        string initialDraft)
    {
        return kind switch
        {
            "table" => BuildTable(data),
            "metric" => BuildMetric(data),
            "list" => BuildList(data),
            "chart" => BuildChartSummary(data),
            "narrative" => BuildTextual(data),
            "badge" => BuildBadge(data),
            "alert" => BuildAlert(data),
            "markdown" => BuildTextual(data),
            "markup" => BuildTextual(data),
            "actions" => BuildActions(setStatusText),
            "text" => BuildTextual(data),
            "searchbox" => BuildSearchBox(data, draftText, setDraftText, statusText, setStatusText, saving, setSaving),
            "selection" => BuildSelection(data, draftText, setDraftText, statusText, setStatusText, saving, setSaving),
            "form" => BuildJsonEditor("form", draftText, setDraftText, initialDraft, statusText, setStatusText, saving, setSaving, parseAsString: false),
            "notes" => BuildJsonEditor("notes", draftText, setDraftText, initialDraft, statusText, setStatusText, saving, setSaving, parseAsString: true),
            "editable-table" => BuildJsonEditor("editable-table", draftText, setDraftText, initialDraft, statusText, setStatusText, saving, setSaving, parseAsString: false),
            "todo" => BuildTodo(data, todoComposerText, setTodoComposerText, statusText, setStatusText, saving, setSaving),
            _ => BuildTextual(data),
        };
    }

    private Element BuildTable(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return BuildMutedText("No data");
        }

        JsonElement first = data.EnumerateArray().First();
        if (first.ValueKind != JsonValueKind.Object)
        {
            return VStack(4, data.EnumerateArray().Select((item, index) =>
                (Element)TextBlock($"{index + 1}. {RenderScalar(item)}").Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)).ToArray());
        }

        string[] columns = first.EnumerateObject().Select(property => property.Name).ToArray();
        var rows = new List<Element>
        {
            Border(TextBlock(string.Join(" | ", columns)).Bold())
                .Padding(6)
                .Background(BoardTheme.ResolveBrush("BoardSurfaceMutedBrush", Colors.White))
                .CornerRadius(8)
        };

        foreach (JsonElement row in data.EnumerateArray())
        {
            string line = string.Join(" | ", columns.Select(column => row.TryGetProperty(column, out JsonElement value) ? RenderScalar(value) : string.Empty));
            rows.Add(TextBlock(line).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords));
        }

        return ScrollViewer(VStack(4, rows.ToArray())).Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto);
    }

    private static Element BuildMetric(JsonElement data)
    {
        string title = string.Empty;
        string value = "-";
        string detail = string.Empty;
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("title", out JsonElement titleElement)) title = RenderScalar(titleElement);
            else if (data.TryGetProperty("label", out JsonElement labelElement)) title = RenderScalar(labelElement);
            if (data.TryGetProperty("value", out JsonElement valueElement)) value = RenderScalar(valueElement);
            if (data.TryGetProperty("detail", out JsonElement detailElement)) detail = RenderScalar(detailElement);
        }
        else if (data.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            value = RenderScalar(data);
        }

        var sections = new List<Element>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            sections.Add(TextBlock(title).Opacity(0.72));
        }

        sections.Add(TextBlock(value).FontSize(28).Bold());
        if (!string.IsNullOrWhiteSpace(detail))
        {
            sections.Add(TextBlock(detail).Opacity(0.72).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords));
        }

        return VStack(4, sections.ToArray());
    }

    private static Element BuildList(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            return VStack(4, data.EnumerateArray().Select(item =>
                (Element)TextBlock($"• {RenderScalar(item)}").Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)).ToArray());
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            return VStack(2, data.EnumerateObject().Select(property =>
                (Element)TextBlock($"{property.Name}: {RenderScalar(property.Value)}").Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)).ToArray());
        }

        return TextBlock(RenderScalar(data)).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);
    }

    private static Element BuildChartSummary(JsonElement data)
    {
        return Border(
                VStack(6,
                    TextBlock("Chart data").Bold(),
                    TextBlock(BuildSummaryText(data)).Opacity(0.72).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    BuildCodeBlock(PrettyJson(data))))
            .Padding(10)
            .Background(BoardTheme.ResolveBrush("BoardSurfaceMutedBrush", Colors.White))
            .CornerRadius(10);
    }

    private static Element BuildTextual(JsonElement data)
    {
        return TextBlock(RenderTextual(data)).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);
    }

    private static Element BuildBadge(JsonElement data)
    {
        return Border(TextBlock(RenderScalar(data)).Bold())
            .Padding(8, 4, 8, 4)
            .Background(BoardTheme.CreateStatusBrush("running", 0x22))
            .CornerRadius(8);
    }

    private static Element BuildAlert(JsonElement data)
    {
        string title = "Alert";
        string body = RenderScalar(data);
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("title", out JsonElement titleElement)) title = RenderScalar(titleElement);
            if (data.TryGetProperty("body", out JsonElement bodyElement)) body = RenderScalar(bodyElement);
        }

        return Border(
                VStack(4,
                    TextBlock(title).Bold(),
                    TextBlock(body).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)))
            .Padding(12)
            .Background(BoardTheme.CreateStatusBrush("failed", 0x22))
            .CornerRadius(10);
    }

    private Element BuildActions(Action<string> setStatusText)
    {
        using JsonDocument renderDef = JsonDocument.Parse(Props.RawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement renderData = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        JsonElement buttons = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("buttons", out JsonElement buttonsElement)
            ? buttonsElement
            : ParseElement(Props.RawDataJson);

        if (buttons.ValueKind != JsonValueKind.Array || buttons.GetArrayLength() == 0)
        {
            return BuildMutedText("No actions");
        }

        return VStack(6,
            HStack(8, buttons.EnumerateArray().Select(button =>
            {
                string label = button.TryGetProperty("label", out JsonElement labelElement)
                    ? RenderScalar(labelElement)
                    : button.TryGetProperty("id", out JsonElement idElement)
                        ? RenderScalar(idElement)
                        : "Action";
                string buttonId = button.TryGetProperty("id", out JsonElement buttonIdElement) ? RenderScalar(buttonIdElement) : label;
                string elemId = root.TryGetProperty("id", out JsonElement elemIdElement) ? RenderScalar(elemIdElement) : string.Empty;
                return (Element)Button(label, () => _ = RunSaveAsync(null, new SaveRequest("actions", null, buttonId, elemId), _ => { }, setStatusText))
                    .AutomationName($"Run {label}")
                    .SubtleButton();
            }).ToArray()),
            string.IsNullOrWhiteSpace(setStatusText.Method.Name) ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed) : TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed));
    }

    private Element BuildSearchBox(JsonElement data, string draftText, Action<string> setDraftText, string statusText, Action<string> setStatusText, bool saving, Action<bool> setSaving)
    {
        SingleFieldConfig? config = GetSingleFieldConfig(Props.RawRenderDefJson, data);
        if (config is null)
        {
            return BuildMutedText("No query field configured");
        }

        return VStack(6,
            TextBox(draftText, setDraftText)
                .AutomationName(config.Field.Title ?? config.FieldKey)
                .PlaceholderText(config.Field.Placeholder ?? config.Field.Title ?? config.FieldKey),
            HStack(8,
                Button(saving ? "Working..." : config.Field.ActionLabel ?? "Search", () =>
                {
                    if (!saving)
                    {
                        object payload = BuildEditorSaveValue(config.WriteTo, config.FieldKey, ConvertFieldText(config.Field, draftText));
                        _ = RunSaveAsync(payload, new SaveRequest("searchbox", config.WriteTo), setSaving, setStatusText);
                    }
                }).AutomationName(config.Field.ActionLabel ?? "Run search").AccentButton(),
                StatusText(statusText)));
    }

    private Element BuildSelection(JsonElement data, string draftText, Action<string> setDraftText, string statusText, Action<string> setStatusText, bool saving, Action<bool> setSaving)
    {
        SingleFieldConfig? config = GetSingleFieldConfig(Props.RawRenderDefJson, data);
        if (config is null)
        {
            return BuildMutedText("No selection configured");
        }

        return VStack(6,
            BuildOptionRows(config.Field.Options, draftText, option =>
            {
                setDraftText(option.Value);
                if (!saving)
                {
                    object payload = BuildEditorSaveValue(config.WriteTo, config.FieldKey, option.Value);
                    _ = RunSaveAsync(payload, new SaveRequest("selection", config.WriteTo), setSaving, setStatusText);
                }
            }),
            StatusText(statusText));
    }

    private Element BuildJsonEditor(string kind, string draftText, Action<string> setDraftText, string initialDraft, string statusText, Action<string> setStatusText, bool saving, Action<bool> setSaving, bool parseAsString)
    {
        bool dirty = !string.Equals(draftText, initialDraft, StringComparison.Ordinal);
        var actionItems = new List<Element>();
        if (dirty)
        {
            actionItems.Add(
                Button("Discard", () => setDraftText(initialDraft))
                    .AutomationName($"Discard {kind}")
                    .SubtleButton());
            actionItems.Add(
                Button(saving ? "Saving..." : "Save", () =>
                {
                    if (!saving)
                    {
                        _ = SaveEditorAsync(kind, draftText, parseAsString, setSaving, setStatusText);
                    }
                })
                    .AutomationName($"Save {kind}")
                    .AccentButton());
        }

        actionItems.Add(StatusText(statusText));

        return VStack(6,
            TextBox(draftText, setDraftText)
                .AutomationName($"{kind} editor")
                .AcceptsReturn(true)
                .TextWrapping(TextWrapping.Wrap)
                .Set(textBox =>
                {
                    textBox.MinHeight = kind == "notes" ? 140 : 180;
                    textBox.FontFamily = new FontFamily("Consolas");
                }),
            HStack(8, actionItems.ToArray()));
    }

    private Element BuildTodo(JsonElement data, string todoComposerText, Action<string> setTodoComposerText, string statusText, Action<string> setStatusText, bool saving, Action<bool> setSaving)
    {
        if (data.ValueKind != JsonValueKind.Array)
        {
            return BuildMutedText("No todo items");
        }

        List<Dictionary<string, object?>> items = data.EnumerateArray().Select(item => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["text"] = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out JsonElement textElement) ? RenderScalar(textElement) : RenderScalar(item),
            ["done"] = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("done", out JsonElement doneElement) && doneElement.ValueKind == JsonValueKind.True
        }).ToList();

        var rows = new List<Element>();
        foreach ((Dictionary<string, object?> item, int index) in items.Select((item, index) => (item, index)))
        {
            bool done = item.TryGetValue("done", out object? doneValue) && doneValue is bool completed && completed;
            string text = item.TryGetValue("text", out object? textValue) ? textValue?.ToString() ?? string.Empty : string.Empty;
            rows.Add(HStack(8,
                Button(done ? "Undo" : "Done", () =>
                {
                    if (!saving)
                    {
                        var nextItems = items.Select(current => new Dictionary<string, object?>(current, StringComparer.Ordinal)).ToList();
                        nextItems[index]["done"] = !done;
                        _ = RunSaveAsync(nextItems, new SaveRequest("todo", ResolveWriteTo(Props.RawRenderDefJson)), setSaving, setStatusText);
                    }
                }).AutomationName($"Toggle todo {text}").SubtleButton(),
                TextBlock(text)
                    .Opacity(done ? 0.58 : 1)
                    .Set(block => block.TextWrapping = TextWrapping.WrapWholeWords)));
        }

        rows.Add(HStack(8,
            TextBox(todoComposerText, setTodoComposerText).AutomationName("Add todo item").PlaceholderText("Add todo item").Flex(grow: 1),
            Button("Add", () =>
            {
                string nextText = todoComposerText.Trim();
                if (!saving && nextText.Length > 0)
                {
                    var nextItems = items.Select(current => new Dictionary<string, object?>(current, StringComparer.Ordinal)).ToList();
                    nextItems.Add(new Dictionary<string, object?> { ["text"] = nextText, ["done"] = false });
                    _ = RunSaveAsync(nextItems, new SaveRequest("todo", ResolveWriteTo(Props.RawRenderDefJson)), setSaving, text =>
                    {
                        setStatusText(text);
                        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "Saved.", StringComparison.Ordinal))
                        {
                            setTodoComposerText(string.Empty);
                        }
                    });
                }
            }).AutomationName("Add todo").AccentButton()));
        rows.Add(StatusText(statusText));
        return VStack(6, rows.ToArray());
    }

    private async Task SaveEditorAsync(string kind, string draftText, bool parseAsString, Action<bool> setSaving, Action<string> setStatusText)
    {
        try
        {
            object? payload = parseAsString ? draftText : ParseLooseJson(draftText);
            await RunSaveAsync(payload, new SaveRequest(kind, ResolveWriteTo(Props.RawRenderDefJson)), setSaving, setStatusText);
        }
        catch (Exception ex)
        {
            setStatusText(ex.Message);
        }
    }

    private async Task RunSaveAsync(object? payload, SaveRequest request, Action<bool> setSaving, Action<string> setStatusText)
    {
        setSaving(true);
        setStatusText("Saving...");
        try
        {
            await HandleSaveAsync(Props.Card, payload, request);
            setStatusText("Saved.");
        }
        catch (Exception ex)
        {
            setStatusText(ex.Message);
        }
        finally
        {
            setSaving(false);
        }
    }

    private static async Task HandleSaveAsync(BoardCard card, object? value, SaveRequest request)
    {
        EmbeddedBoardClient client = App.Current.BoardClient;
        if (string.Equals(request.Kind, "actions", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(request.ButtonId))
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

    private static SingleFieldConfig? GetSingleFieldConfig(string rawRenderDefJson, JsonElement data)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement renderData = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        string? writeTo = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("writeTo", out JsonElement writeToElement)
            ? RenderScalar(writeToElement)
            : null;
        JsonElement fields = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("fields", out JsonElement fieldsElement) ? fieldsElement : default;
        JsonElement properties = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("properties", out JsonElement propertiesElement) ? propertiesElement : default;
        if (properties.ValueKind != JsonValueKind.Object || properties.EnumerateObject().Count() != 1)
        {
            return null;
        }

        JsonProperty prop = properties.EnumerateObject().First();
        string fieldKey = prop.Name;
        object? currentValue = ResolveSingleFieldCurrentValue(writeTo, fieldKey, rawRenderDefJson, data);
        return new SingleFieldConfig(writeTo, fieldKey, currentValue, BuildFieldConfig(fieldKey, prop.Value, renderData, data));
    }

    private static object? ResolveSingleFieldCurrentValue(string? writeTo, string fieldKey, string rawRenderDefJson, JsonElement data)
    {
        if (TryGetResolvedWriteValue(rawRenderDefJson, out JsonElement resolvedWriteValue))
        {
            if (string.Equals(writeTo, "card_data", StringComparison.Ordinal)
                && resolvedWriteValue.ValueKind == JsonValueKind.Object
                && resolvedWriteValue.TryGetProperty(fieldKey, out JsonElement fieldValue))
            {
                return ConvertJsonElement(fieldValue);
            }

            return ConvertJsonElement(resolvedWriteValue);
        }

        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(fieldKey, out JsonElement valueElement))
        {
            return ConvertJsonElement(valueElement);
        }

        return ConvertJsonElement(data);
    }

    private static FieldConfig BuildFieldConfig(string key, JsonElement property, JsonElement renderData, JsonElement data)
    {
        string? title = property.TryGetProperty("title", out JsonElement titleElement) ? RenderScalar(titleElement) : key;
        string? type = property.TryGetProperty("type", out JsonElement typeElement) ? RenderScalar(typeElement) : null;
        string? placeholder = property.TryGetProperty("placeholder", out JsonElement placeholderElement) ? RenderScalar(placeholderElement) : null;
        var options = new List<ChoiceOption>();
        if (property.TryGetProperty("enum", out JsonElement enumElement) && enumElement.ValueKind == JsonValueKind.Array)
        {
            options.AddRange(enumElement.EnumerateArray().Select(option =>
            {
                string value = RenderScalar(option);
                return new ChoiceOption(value, value);
            }));
        }
        else if (data.ValueKind == JsonValueKind.Array)
        {
            options.AddRange(data.EnumerateArray().Select(option =>
            {
                string value = RenderScalar(option);
                return new ChoiceOption(value, value);
            }));
        }

        string? actionLabel = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("actionLabel", out JsonElement actionLabelElement)
            ? RenderScalar(actionLabelElement)
            : null;

        return new FieldConfig(key, title, type, placeholder, options, actionLabel);
    }

    private static bool TryGetResolvedWriteValue(string rawRenderDefJson, out JsonElement resolvedWriteValue)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        if (root.TryGetProperty("resolvedWriteValue", out JsonElement element))
        {
            resolvedWriteValue = JsonDocument.Parse(element.GetRawText()).RootElement.Clone();
            return true;
        }

        resolvedWriteValue = default;
        return false;
    }

    private static string NormalizeLegacyKind(string kind, string rawRenderDefJson, JsonElement data)
    {
        if (string.Equals(kind, "query", StringComparison.OrdinalIgnoreCase)) return "searchbox";
        if (!string.Equals(kind, "filter", StringComparison.OrdinalIgnoreCase)) return kind;

        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement renderDefRoot = renderDef.RootElement;
        JsonElement renderData = renderDefRoot.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        JsonElement fields = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("fields", out JsonElement fieldsElement) ? fieldsElement : default;
        JsonElement properties = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("properties", out JsonElement propertiesElement) ? propertiesElement : default;
        if (properties.ValueKind != JsonValueKind.Object || properties.EnumerateObject().Count() != 1)
        {
            return "form";
        }

        JsonProperty prop = properties.EnumerateObject().First();
        if ((prop.Value.TryGetProperty("enum", out JsonElement enumElement) && enumElement.ValueKind == JsonValueKind.Array)
            || data.ValueKind == JsonValueKind.Array)
        {
            return "selection";
        }

        if (!prop.Value.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || string.Equals(typeElement.GetString(), "string", StringComparison.OrdinalIgnoreCase))
        {
            return "searchbox";
        }

        return "form";
    }

    private static string CreateInitialDraft(string kind, JsonElement data)
    {
        if (kind == "notes")
        {
            return data.ValueKind == JsonValueKind.String ? data.GetString() ?? string.Empty : string.Empty;
        }

        if (kind == "searchbox" || kind == "selection")
        {
            return data.ValueKind == JsonValueKind.String ? data.GetString() ?? string.Empty : RenderScalar(data);
        }

        if (kind is "form" or "editable-table")
        {
            return PrettyJson(data);
        }

        return RenderTextual(data);
    }

    private static Element BuildOptionRows(IReadOnlyList<ChoiceOption> options, string selectedValue, Action<ChoiceOption> onSelect)
    {
        if (options.Count == 0)
        {
            return BuildMutedText("No options");
        }

        return VStack(6, Chunk(options, 3).Select(chunk =>
            (Element)HStack(8, chunk.Select(option =>
                (Element)Button(
                        Border(TextBlock(option.Label))
                            .Padding(10, 4, 10, 4)
                            .Background(BoardTheme.CreateStatusBrush(string.Equals(option.Value, selectedValue, StringComparison.Ordinal) ? "running" : "fresh", string.Equals(option.Value, selectedValue, StringComparison.Ordinal) ? (byte)0x24 : (byte)0x12))
                            .WithBorder(BoardTheme.CreateStatusBrush(string.Equals(option.Value, selectedValue, StringComparison.Ordinal) ? "running" : "fresh", string.Equals(option.Value, selectedValue, StringComparison.Ordinal) ? (byte)0x88 : (byte)0x44), 1)
                            .CornerRadius(12),
                        () => onSelect(option))
                    .AutomationName($"Select {option.Label}")
                    .SubtleButton()).ToArray())
        ).ToArray());
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (int index = 0; index < source.Count; index += size)
        {
            yield return source.Skip(index).Take(size).ToArray();
        }
    }

    private static Element StatusText(string statusText)
    {
        return string.IsNullOrWhiteSpace(statusText)
            ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
            : TextBlock(statusText)
                .Opacity(0.82)
                .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);
    }

    private static Element BuildCodeBlock(string value)
    {
        return TextBox(value)
            .AutomationName("Code block")
            .IsReadOnly(true)
            .AcceptsReturn(true)
            .TextWrapping(TextWrapping.Wrap)
            .Set(textBox =>
            {
                textBox.MinHeight = 120;
                textBox.FontFamily = new FontFamily("Consolas");
            });
    }

    private static Element BuildMutedText(string text)
    {
        return TextBlock(text).Opacity(0.62).Set(block => block.TextWrapping = TextWrapping.WrapWholeWords);
    }

    private static JsonElement ParseElement(string rawJson)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(rawJson) ? "null" : rawJson).RootElement.Clone();
        }
        catch
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(rawJson ?? string.Empty)).RootElement.Clone();
        }
    }

    private static object? ParseLooseJson(string rawJson)
    {
        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawJson) ? "null" : rawJson);
        return ConvertJsonElement(document.RootElement);
    }

    private static object BuildEditorSaveValue(string? writeTo, string? fieldKey, object? nextValue)
    {
        if (string.Equals(writeTo, "card_data", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(fieldKey))
        {
            return new Dictionary<string, object?> { [fieldKey] = nextValue };
        }

        return nextValue ?? string.Empty;
    }

    private static string? ResolveWriteTo(string rawRenderDefJson)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement renderData = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        return renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("writeTo", out JsonElement writeToElement)
            ? RenderScalar(writeToElement)
            : null;
    }

    private static object? ConvertFieldText(FieldConfig field, string? rawValue)
    {
        string value = rawValue ?? string.Empty;
        if (string.Equals(field.Type, "number", StringComparison.OrdinalIgnoreCase))
        {
            return double.TryParse(value, out double parsedNumber) ? parsedNumber : 0d;
        }

        if (string.Equals(field.Type, "integer", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value, out long parsedInteger) ? parsedInteger : 0L;
        }

        return value;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out long intValue) => intValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    private static string PrettyJson(JsonElement value)
    {
        try
        {
            return JsonSerializer.Serialize(value, PrettyJsonOptions);
        }
        catch
        {
            return value.GetRawText();
        }
    }

    private static string RenderTextual(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.Object or JsonValueKind.Array => PrettyJson(value),
            _ => value.GetRawText()
        };
    }

    private static string RenderScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };
    }

    private static string BuildSummaryText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => $"{value.GetArrayLength()} row(s)",
            JsonValueKind.Object => $"{value.EnumerateObject().Count()} field(s)",
            JsonValueKind.String => RenderScalar(value),
            _ => value.GetRawText()
        };
    }

    private static string[] PathParts(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? Array.Empty<string>()
            : path.Replace("[", ".").Replace("]", string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
    }
}