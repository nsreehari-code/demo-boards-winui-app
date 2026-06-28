using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

/// <summary>
/// Mirrors <c>TemplateCardIngest.jsx</c> — select and button for ingesting template cards.
/// </summary>
public sealed record TemplateCardIngestProps(
    IReadOnlyList<object?>? Entries = null,
    string SelectedKey = "",
    Action<string>? OnSelect = null,
    Action? OnIngest = null,
    bool Loading = false,
    bool Ingesting = false,
    bool Preparing = false,
    string ErrorMessage = "",
    bool Disabled = false);

public sealed class TemplateCardIngest : Component<TemplateCardIngestProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var entries = Props.Entries ?? Array.Empty<object?>();
        bool hasEntries = entries.Count > 0;

        // Map entries from {key, label} to {value, label} for SelectControl
        var selectOptions = new List<object?>();
        foreach (var e in entries)
        {
            if (e is IDictionary<string, object?> entryDict)
            {
                selectOptions.Add(new Dictionary<string, object?>
                {
                    ["value"] = entryDict.TryGetValue("key", out var k) ? k : "",
                    ["label"] = entryDict.TryGetValue("label", out var l) ? l : "",
                });
            }
        }

        return VStack(12,
            TextBlock("Template Card Ingest")
                .FontSize(14)
                .Bold(),
            HStack(8,
                Component<SelectControl, SelectControlProps>(new SelectControlProps(
                    Value: Props.SelectedKey,
                    Options: selectOptions,
                    AllowEmpty: !hasEntries,
                    EmptyLabel: Props.Loading ? "Loading seed boards…" : "No seed boards available",
                    Disabled: Props.Loading || Props.Ingesting || Props.Preparing || !hasEntries,
                    AriaLabel: "Select a bundled sample board file",
                    Title: Props.ErrorMessage ?? "Select a bundled sample board file",
                    OnChange: Props.OnSelect
                )).Flex(grow: 1),
                Button(Props.Preparing ? "Preparing…" : Props.Ingesting ? "Ingesting…" : "Ingest Cards", Props.OnIngest)
                    .IsEnabled(!(Props.Ingesting || Props.Preparing || Props.Disabled || string.IsNullOrEmpty(Props.SelectedKey) || Props.Loading || !hasEntries))
                    .AutomationName("Ingest template cards into board")
                    .SubtleButton()
            )
        )
        .Set(stack => stack.Padding = new(12))
        .Set(stack => stack.BorderThickness = new(1))
        .Set(stack =>
        {
            stack.BorderBrush = theme.CardBorder;
            stack.Background = theme.SurfaceElevated;
        });
    }
}
