using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

/// <summary>
/// Mirrors <c>ConfigSubPane.jsx</c> — a full-bleed sub-view that takes over the board settings panel.
/// Renders a header with a Back control + title and the provided children as a scrollable body.
/// </summary>
public sealed record ConfigSubPaneProps(
    string Title = "",
    Action? OnBack = null,
    Element[]? Children = null);

public sealed class ConfigSubPane : Component<ConfigSubPaneProps>
{
    public override Element Render()
    {
        var children = Props.Children ?? Array.Empty<Element>();

        return VStack(0,
            // Header with Back button and title
            HStack(12,
                Button(HStack(6,
                    Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ChevronLeft, 12)),
                    TextBlock("Back")), Props.OnBack ?? (() => { }))
                    .AutomationName("Back to board settings")
                    .SubtleButton()
                    .Flex(shrink: 0),
                TextBlock(Props.Title)
                    .FontSize(18)
                    .Bold()
                    .Flex(grow: 1))
                .Flex(shrink: 0)
                .Set(stack => stack.Padding = new(12, 8, 12, 8))
                .Set(stack => stack.Background = new SolidColorBrush(BoardShared.ToneColor("surface-elevated"))),

            // Scrollable body
            VStack(12, children)
                .Flex(grow: 1)
                .Set(stack => stack.Padding = new(12))
        )
        .Flex(grow: 1);
    }
}
