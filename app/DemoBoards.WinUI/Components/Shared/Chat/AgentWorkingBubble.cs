using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Windows.UI.Text;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>AgentWorkingBubble.jsx</c> — the "AI working…" live-activity bubble. Fully
/// presentational and prop-driven: it takes the latest agent <c>AgentOutput</c>/<c>AgentTools</c>
/// text and renders the working indicator plus optional activity chips (clicking a chip expands its
/// full text). It holds only view state (which chip is open and the randomly-picked chip labels) and
/// reads no chat data of its own.
/// </summary>
public sealed record AgentWorkingBubbleProps(
    string AgentOutput = "",
    string AgentTools = "",
    bool Compact = false);

public sealed class AgentWorkingBubble : Component<AgentWorkingBubbleProps>
{
    private static readonly string[] ProcessingStates =
    {
        "The mission is underway…",
        "Engaging hyperdrive…",
        "Activating mission protocols…",
        "Calculating the jump…",
        "Scanning the galaxy…",
        "The Force is in motion…",
        "Forces are at work…",
    };

    private static readonly string[] ToolStates =
    {
        "Chewie, get us ready…",
        "Summoning the council…",
        "R2 is working on it…",
        "Summoning the squadron…",
        "Deploying the squadron…",
        "Calling in support…",
        "Tactical units mobilised",
        "Companions joining",
        "Power is gathering",
    };

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (activeChipKey, setActiveChipKey) = UseState(string.Empty);

        // Pick the shimmer labels once and keep them stable across renders (lazy init via ref).
        var labelsRef = UseRef<(string Output, string Tools)?>(null);
        labelsRef.Current ??= (PickRandom(ProcessingStates), PickRandom(ToolStates));
        (string Output, string Tools) labels = labelsRef.Current.Value;

        string liveOutput = Props.AgentOutput ?? string.Empty;
        string liveTools = Props.AgentTools ?? string.Empty;

        var chips = new List<(string Key, string Label, string Value)>();
        if (!string.IsNullOrWhiteSpace(liveOutput))
        {
            chips.Add(("output", labels.Output, ToChipPreview(liveOutput)));
        }

        if (!string.IsNullOrWhiteSpace(liveTools))
        {
            chips.Add(("tools", labels.Tools, ToChipPreview(liveTools)));
        }

        var children = new List<Element>
        {
            HStack(6,
                ChatBubble.ChatIconShell(WorkingBubbleIcon()),
                TextBlock("AI working...")
                    .FontSize(12)
                    .Foreground(theme.TextPrimary)
                    .Set(text => text.FontStyle = FontStyle.Italic),
                ProgressRing().Width(14).Height(14)),
        };

        if (chips.Count > 0)
        {
            Element[] chipButtons = chips.Select(chip =>
            {
                bool active = activeChipKey == chip.Key;
                string label = string.IsNullOrEmpty(chip.Value) ? chip.Label : $"{chip.Label}   {chip.Value}";
                string key = chip.Key;
                return (Element)(active
                    ? Button(label, () => setActiveChipKey(string.Empty)).AccentButton().AutomationName(chip.Label)
                    : Button(label, () => setActiveChipKey(key)).SubtleButton().AutomationName(chip.Label));
            }).ToArray();
            children.Add(VStack(4, chipButtons));
        }

        (string Key, string Label, string Value) activeChip = chips.FirstOrDefault(chip => chip.Key == activeChipKey);
        if (activeChip.Key != null && !string.IsNullOrEmpty(activeChip.Value))
        {
            children.Add(Border(TextBlock(activeChip.Value)
                    .FontSize(12)
                    .Foreground(theme.TextPrimary)
                    .TextWrapping(TextWrapping.Wrap)
                    .Set(text => text.FontStyle = FontStyle.Italic))
                .Padding(8)
                .Background(theme.Layer)
                .WithBorder(theme.CardBorder, 1)
                .CornerRadius(6));
        }

        return Border(VStack(6, children.ToArray()))
            .Padding(8)
            .Background(theme.LayerAlt)
            .CornerRadius(12)
            .HAlign(HorizontalAlignment.Left);
    }

    /// <summary>Default glyph for the working bubble avatar — mirrors <c>WorkingBubbleIcon</c>.</summary>
    private static Element WorkingBubbleIcon() => Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ChatWorkingBubble, 14));

    private static string PickRandom(string[] options) => options[Random.Shared.Next(options.Length)];

    /// <summary>Last non-empty line of the agent text, trimmed — mirrors <c>toChipPreview</c>.</summary>
    private static string ToChipPreview(string text)
    {
        string source = text ?? string.Empty;
        string? lastNonEmpty = source
            .Replace("\r\n", "\n")
            .Split('\n')
            .Reverse()
            .FirstOrDefault(line => line.Trim().Length > 0);
        return (lastNonEmpty ?? source).Trim();
    }
}
