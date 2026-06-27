using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Card preflight panel (port of <c>card/sub/CardPreflight.jsx</c>) — the inspect sidebar's top pane.
/// It reads the raw card definition (requires / provides / source_defs / view elements) and renders the
/// card's dependency tokens (clickable to inspect their live data object), an expandable YAML summary per
/// external source (each with a run-flight button), the distinct rendered view-element kinds, plus a
/// run-card-preflight button on the title. DOM-only concerns (per-token YAML key/colon/value span colouring,
/// bootstrap chevron/flask icon classes, className/style hooks) are dropped — the YAML lines render as plain
/// monospace text and the toggle/flight controls use glyph buttons.
/// </summary>
public sealed record CardPreflightProps(
    string CardId,
    IReadOnlyDictionary<string, object?> CardContent,
    IReadOnlyDictionary<int, bool>? LoadingBySource = null,
    bool CardFlightLoading = false,
    bool FlightDisabled = false,
    Action? OnRunCardFlight = null,
    Action<int, string>? OnRunFlight = null,
    Action<string, string>? OnInspectToken = null,
    string ActiveTokenKey = "");

public sealed class CardPreflight : HookComponent<CardPreflightProps>
{
    private static readonly HashSet<string> SourceSummaryExcludedKeys = new(StringComparer.Ordinal)
    {
        "bindTo",
        "outputFile",
        "projections",
        "optionalForCompletionGating",
        "timeout",
        "script",
    };

    /// <summary>One external source's collapsed summary (port of <c>buildSourceSummary</c>).</summary>
    public readonly record struct SourceSummary(string Id, int Index, string BindTo, IReadOnlyList<string> DetailLines);

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        IReadOnlyList<object?> requires = BoardData.ToList(BoardData.Get(Props.CardContent, "requires")) ?? Array.Empty<object?>();
        IReadOnlyList<object?> provides = BoardData.ToList(BoardData.Get(Props.CardContent, "provides")) ?? Array.Empty<object?>();
        IReadOnlyList<object?> sourceDefs = BoardData.ToList(BoardData.Get(Props.CardContent, "source_defs")) ?? Array.Empty<object?>();
        IReadOnlyDictionary<string, object?> view = BoardData.AsMap(BoardData.Get(Props.CardContent, "view"));
        IReadOnlyList<object?> viewElements = BoardData.ToList(BoardData.Get(view, "elements")) ?? Array.Empty<object?>();

        IReadOnlyList<string> requireTokens = requires.Select(BoardValues.Stringify).ToList();
        IReadOnlyList<string> provideTokens = provides
            .Select(item => BoardData.Str(BoardData.AsMap(item), "bindTo") ?? string.Empty)
            .ToList();
        IReadOnlyList<string> renderedViews = DistinctViewKinds(viewElements);
        var sourceSummaries = new List<SourceSummary>(sourceDefs.Count);
        for (int index = 0; index < sourceDefs.Count; index++)
        {
            sourceSummaries.Add(BuildSourceSummary(sourceDefs[index], index));
        }

        var children = new List<Element>
        {
            HStack(8,
                TextBlock(Props.CardId).Bold().FontSize(13).Foreground(theme.TextPrimary)
                    .Set(t => t.TextTrimming = TextTrimming.CharacterEllipsis).Flex(grow: 1),
                Props.OnRunCardFlight is not null
                    ? FlightButton("Run card preflight", () => Props.OnRunCardFlight!(), Props.CardFlightLoading,
                        Props.FlightDisabled || Props.CardFlightLoading, theme)
                    : Empty()),
        };

        if (requireTokens.Count > 0 || provideTokens.Count > 0)
        {
            children.Add(HStack(12,
                TokenChipSection("Depends On", requireTokens, "require", theme).Flex(grow: 1),
                TokenChipSection("Produces", provideTokens, "provide", theme).Flex(grow: 1)));
        }

        if (sourceSummaries.Count > 0)
        {
            var blocks = new List<Element> { SectionTitle("External Data", theme) };
            foreach (SourceSummary summary in sourceSummaries)
            {
                blocks.Add(Component<SourceBlock, SourceBlockProps>(new SourceBlockProps(
                    summary,
                    Props.OnRunFlight,
                    Props.LoadingBySource is not null && Props.LoadingBySource.TryGetValue(summary.Index, out bool loading) && loading,
                    Props.FlightDisabled)));
            }

            children.Add(VStack(6, blocks.ToArray()));
        }

        if (renderedViews.Count > 0)
        {
            var tokens = new List<Element> { SectionTitle("Rendered Card Elements", theme) };
            tokens.Add(WrapChips(renderedViews.Select(kind => Chip(kind, theme)).ToArray()));
            children.Add(VStack(6, tokens.ToArray()));
        }

        if (requireTokens.Count == 0 && sourceSummaries.Count == 0 && provideTokens.Count == 0)
        {
            children.Add(TextBlock("No configuration found.").FontSize(12).Opacity(0.7).Foreground(theme.TextPrimary));
        }

        return VStack(10, children.ToArray());
    }

    private Element TokenChipSection(string title, IReadOnlyList<string> tokens, string kind, AppTheme theme)
    {
        if (tokens.Count == 0)
        {
            return Empty();
        }

        var chips = new List<Element>(tokens.Count);
        foreach (string value in tokens)
        {
            bool isActive = Props.ActiveTokenKey == $"{kind}:{value}";
            chips.Add(Props.OnInspectToken is not null
                ? ChipButton(value, () => Props.OnInspectToken!(value, kind), isActive, theme)
                : Chip(value, theme));
        }

        return VStack(4, SectionTitle(title, theme), WrapChips(chips.ToArray()));
    }

    // ----- pure helpers (faithful ports of the module-level functions) -----

    /// <summary>Distinct, order-preserving non-empty <c>element.kind</c> values (port of <c>renderedViews</c>).</summary>
    public static IReadOnlyList<string> DistinctViewKinds(IReadOnlyList<object?> viewElements)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (object? element in viewElements)
        {
            string kind = (BoardData.Str(BoardData.AsMap(element), "kind") ?? string.Empty).Trim();
            if (kind.Length > 0 && seen.Add(kind))
            {
                result.Add(kind);
            }
        }

        return result;
    }

    /// <summary>Renders a scalar the way the web's <c>formatScalar</c> did (objects fall back to compact JSON).</summary>
    public static string FormatScalar(object? value) => value switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        double or float or int or long or decimal => BoardValues.Stringify(value),
        _ => CompactJson(value),
    };

    /// <summary>Flattens a value into indented YAML-style lines (port of <c>toYamlLines</c>).</summary>
    public static IReadOnlyList<string> ToYamlLines(object? value, int indent = 0)
    {
        string pad = new(' ', indent);
        var lines = new List<string>();

        IReadOnlyList<object?>? list = value as IReadOnlyList<object?> ?? (value is string ? null : BoardData.ToList(value));
        if (list is not null)
        {
            if (list.Count == 0)
            {
                lines.Add($"{pad}[]");
                return lines;
            }

            foreach (object? item in list)
            {
                if (IsObject(item))
                {
                    lines.Add($"{pad}-");
                    lines.AddRange(ToYamlLines(item, indent + 2));
                }
                else
                {
                    lines.Add($"{pad}- {FormatScalar(item)}");
                }
            }

            return lines;
        }

        if (value is IReadOnlyDictionary<string, object?> map)
        {
            if (map.Count == 0)
            {
                lines.Add($"{pad}{{}}");
                return lines;
            }

            foreach (KeyValuePair<string, object?> entry in map)
            {
                if (IsObject(entry.Value))
                {
                    lines.Add($"{pad}{entry.Key}:");
                    lines.AddRange(ToYamlLines(entry.Value, indent + 2));
                }
                else
                {
                    lines.Add($"{pad}{entry.Key}: {FormatScalar(entry.Value)}");
                }
            }

            return lines;
        }

        lines.Add($"{pad}{FormatScalar(value)}");
        return lines;
    }

    /// <summary>Builds the collapsed YAML summary for one source definition (port of <c>buildSourceSummary</c>).</summary>
    public static SourceSummary BuildSourceSummary(object? sourceDef, int index)
    {
        IReadOnlyDictionary<string, object?> def = BoardData.AsMap(sourceDef);
        string? kindKey = def.Keys.FirstOrDefault(key => !SourceSummaryExcludedKeys.Contains(key) && !key.StartsWith('_'));
        object? kindValue = kindKey is not null ? BoardData.Get(def, kindKey) : null;
        IReadOnlyDictionary<string, object?>? projections = BoardData.Get(def, "projections") is IReadOnlyDictionary<string, object?> p ? p : null;

        var yamlLines = new List<string>();
        if (kindKey is not null)
        {
            yamlLines.Add($"{kindKey}:");
            yamlLines.AddRange(ToYamlLines(kindValue, 2));
        }

        if (projections is not null && projections.Count > 0)
        {
            yamlLines.Add("projections:");
            yamlLines.AddRange(ToYamlLines(projections, 2));
        }

        if (yamlLines.Count == 0)
        {
            yamlLines.Add("no source definition details");
        }

        return new SourceSummary($"source-{index}", index, BoardData.Str(def, "bindTo") ?? string.Empty, yamlLines);
    }

    private static bool IsObject(object? value) =>
        value is IReadOnlyDictionary<string, object?> || (value is not string && BoardData.ToList(value) is not null);

    private static string CompactJson(object? value) => ToJsonNode(value)?.ToJsonString() ?? "null";

    internal static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        string s => JsonValue.Create(s),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create((double)f),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        decimal m => JsonValue.Create(m),
        IReadOnlyDictionary<string, object?> map => ToJsonObject(map),
        System.Collections.IEnumerable seq => ToJsonArray(seq),
        _ => JsonValue.Create(value.ToString()),
    };

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, object?> map)
    {
        var obj = new JsonObject();
        foreach (KeyValuePair<string, object?> entry in map)
        {
            obj[entry.Key] = ToJsonNode(entry.Value);
        }

        return obj;
    }

    private static JsonArray ToJsonArray(System.Collections.IEnumerable seq)
    {
        var arr = new JsonArray();
        foreach (object? item in seq)
        {
            arr.Add(ToJsonNode(item));
        }

        return arr;
    }

    // ----- presentational chrome helpers -----

    internal static Element SectionTitle(string title, AppTheme theme) =>
        TextBlock(title).FontSize(11).Bold().Opacity(0.7).Foreground(theme.TextPrimary);

    internal static Element Chip(string value, AppTheme theme) =>
        Border(TextBlock(value).FontSize(11).Foreground(theme.TextPrimary))
            .Padding(4)
            .Background(theme.LayerAlt)
            .CornerRadius(6);

    internal static Element ChipButton(string value, Action onClick, bool isActive, AppTheme theme) =>
        Button(value, onClick)
            .SubtleButton()
            .AutomationName(value)
            .Set(b =>
            {
                b.FontSize = 11;
                b.Opacity = isActive ? 1 : 0.92;
                b.BorderBrush = isActive ? theme.Accent : theme.CardBorder;
                b.BorderThickness = new Thickness(1);
                b.CornerRadius = new CornerRadius(6);
            });

    internal static Element WrapChips(Element[] chips) => HStack(6, chips);

    internal static Element FlightButton(string title, Action onClick, bool loading, bool disabled, AppTheme theme) =>
        loading
            ? ProgressRing().AutomationName(title)
            : Button("\u2697", onClick).SubtleButton().AutomationName(title).Set(b => b.IsEnabled = !disabled);
}

/// <summary>Props for <see cref="SourceBlock"/> — one expandable source summary row.</summary>
public sealed record SourceBlockProps(
    CardPreflight.SourceSummary Summary,
    Action<int, string>? OnRunFlight,
    bool IsLoading,
    bool Disabled);

/// <summary>Expandable external-source row (port of <c>SourceBlock</c>) with its run-flight button.</summary>
public sealed class SourceBlock : HookComponent<SourceBlockProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        (bool expanded, Action<bool> setExpanded) = UseState(false);

        string bind = Props.Summary.BindTo.Length > 0 ? Props.Summary.BindTo : "unbound";

        Element header = HStack(6,
            Button($"{(expanded ? "\u25BE" : "\u25B8")} {bind}", () => setExpanded(!expanded))
                .SubtleButton()
                .AutomationName(expanded ? "Collapse source details" : "Expand source details")
                .Set(b =>
                {
                    b.FontSize = 11;
                    b.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                })
                .Flex(grow: 1),
            Props.OnRunFlight is not null
                ? CardPreflight.FlightButton(
                    $"Run source flight: {(Props.Summary.BindTo.Length > 0 ? Props.Summary.BindTo : "source")}",
                    () => Props.OnRunFlight!(Props.Summary.Index, Props.Summary.BindTo),
                    Props.IsLoading,
                    Props.Disabled || Props.IsLoading,
                    theme)
                : Empty());

        if (!expanded)
        {
            return header;
        }

        var lines = new List<Element>(Props.Summary.DetailLines.Count);
        foreach (string line in Props.Summary.DetailLines)
        {
            lines.Add(TextBlock(line).FontSize(11).Foreground(theme.TextPrimary)
                .Set(t => t.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")));
        }

        return VStack(4,
            header,
            Border(VStack(2, lines.ToArray()))
                .Padding(8)
                .Background(theme.Layer)
                .CornerRadius(6));
    }
}
