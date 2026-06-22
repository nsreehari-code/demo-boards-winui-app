using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using DemoBoards.RuntimeHost;

namespace DemoBoards_WinUI.Controls;

public sealed record PaneRule(string Pane, string? When);

public sealed record RendererRule(string Renderer, string? When);

public static class CardPresentationConfig
{
    private static readonly Regex EqualityRulePattern = new("^meta\\.(?<key>[A-Za-z0-9_-]+)\\s*=\\s*(?<value>true|false|\"[^\"]*\"|'[^']*')$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<PaneRule> DefaultPaneRules { get; } = new[]
    {
        new PaneRule("gandalf", "meta.gandalf = true"),
        new PaneRule("truthset", "meta.truthset = true"),
    };

    public static IReadOnlyList<RendererRule> DefaultRendererRules { get; } = new[]
    {
        new RendererRule("strategist", "meta.card_renderer = \"strategist\""),
        new RendererRule("ingest", "meta.card_renderer = \"ingest\""),
        new RendererRule("postbox", "meta.card_renderer = \"postbox\""),
    };

    public static IReadOnlyList<Func<BoardCard, bool>> ResolvePaneFilters(string paneName, IReadOnlyList<PaneRule>? rules = null)
    {
        var sourceRules = rules ?? DefaultPaneRules;
        var filters = new List<Func<BoardCard, bool>>();
        foreach (PaneRule rule in sourceRules)
        {
            if (!string.Equals(rule.Pane, paneName, StringComparison.Ordinal))
            {
                continue;
            }

            filters.Add(CompileRule(rule.When));
        }

        return filters;
    }

    public static IReadOnlyList<Func<BoardCard, bool>> ResolvePaneFilters(string paneName, string? uiConfigJson)
    {
        return ResolvePaneFilters(paneName, ResolvePaneRulesConfig(uiConfigJson));
    }

    public static IReadOnlyList<RendererRule> CompileRendererRules(IReadOnlyList<RendererRule>? rules = null)
    {
        return rules ?? DefaultRendererRules;
    }

    public static IReadOnlyList<RendererRule> CompileRendererRules(string? uiConfigJson)
    {
        return ResolveRendererRulesConfig(uiConfigJson);
    }

    public static string ResolveCardRenderer(BoardCard card, IReadOnlyList<RendererRule>? rules = null)
    {
        foreach (RendererRule rule in CompileRendererRules(rules))
        {
            if (CompileRule(rule.When)(card))
            {
                return rule.Renderer;
            }
        }

        return "default";
    }

    private static Func<BoardCard, bool> CompileRule(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return _ => true;
        }

        Match match = EqualityRulePattern.Match(expression.Trim());
        if (!match.Success)
        {
            return _ => false;
        }

        string key = match.Groups["key"].Value;
        string expectedRaw = match.Groups["value"].Value;
        string expected = NormalizeExpectedValue(expectedRaw);
        return card => card.MetaValues.TryGetValue(key, out string? value)
            && string.Equals(NormalizeExpectedValue(value), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExpectedValue(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if ((text.StartsWith("\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal))
            || (text.StartsWith("'", StringComparison.Ordinal) && text.EndsWith("'", StringComparison.Ordinal)))
        {
            return text[1..^1];
        }

        return text;
    }

    private static IReadOnlyList<PaneRule> ResolvePaneRulesConfig(string? uiConfigJson)
    {
        JsonElement uiConfig = ParseUiConfig(uiConfigJson);
        if (uiConfig.ValueKind != JsonValueKind.Object
            || !uiConfig.TryGetProperty("paneRules", out JsonElement paneRules)
            || paneRules.ValueKind != JsonValueKind.Array)
        {
            return DefaultPaneRules;
        }

        var normalized = new List<PaneRule>();
        foreach (JsonElement rule in paneRules.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string pane = GetString(rule, "pane") ?? GetString(rule, "name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pane))
            {
                continue;
            }

            normalized.Add(new PaneRule(pane.Trim(), GetString(rule, "when")));
        }

        return normalized.Count > 0 ? normalized : DefaultPaneRules;
    }

    private static IReadOnlyList<RendererRule> ResolveRendererRulesConfig(string? uiConfigJson)
    {
        JsonElement uiConfig = ParseUiConfig(uiConfigJson);
        if (uiConfig.ValueKind != JsonValueKind.Object
            || !uiConfig.TryGetProperty("cardRendererRules", out JsonElement rendererRules))
        {
            return DefaultRendererRules;
        }

        if (rendererRules.ValueKind == JsonValueKind.Array)
        {
            var fromArray = new List<RendererRule>();
            foreach (JsonElement rule in rendererRules.EnumerateArray())
            {
                RendererRule? normalized = NormalizeRendererRule(rule);
                if (normalized is not null)
                {
                    fromArray.Add(normalized);
                }
            }

            return fromArray.Count > 0 ? fromArray : DefaultRendererRules;
        }

        if (rendererRules.ValueKind == JsonValueKind.Object)
        {
            var fromObject = new List<RendererRule>();
            foreach (JsonProperty property in rendererRules.EnumerateObject())
            {
                string renderer = property.Name?.Trim() ?? string.Empty;
                if (renderer.Length == 0)
                {
                    continue;
                }

                string? when = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
                fromObject.Add(new RendererRule(renderer, when));
            }

            return fromObject.Count > 0 ? fromObject : DefaultRendererRules;
        }

        return DefaultRendererRules;
    }

    private static RendererRule? NormalizeRendererRule(JsonElement rule)
    {
        if (rule.ValueKind == JsonValueKind.String)
        {
            string rendererName = rule.GetString()?.Trim() ?? string.Empty;
            return rendererName.Length == 0 ? null : new RendererRule(rendererName, null);
        }

        if (rule.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string renderer = GetString(rule, "renderer") ?? GetString(rule, "name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(renderer))
        {
            return null;
        }

        return new RendererRule(renderer.Trim(), GetString(rule, "when"));
    }

    private static JsonElement ParseUiConfig(string? uiConfigJson)
    {
        if (string.IsNullOrWhiteSpace(uiConfigJson))
        {
            return default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(uiConfigJson);
            return document.RootElement.Clone();
        }
        catch
        {
            return default;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}