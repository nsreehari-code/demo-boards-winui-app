using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using Windows.UI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>PanelVertical.jsx</c> — a full-height side rail that composes a
/// <see cref="FloatingCircularButton"/> toggle with a scrollable panel. <c>Expanded</c> drives whether
/// the panel content shows; <c>FabPosition</c> selects which corner the FAB pins to.
/// </summary>
public sealed record PanelVerticalProps(
    string FabPosition = "top-left",
    bool Expanded = false,
    Action? OnToggle = null,
    string? AriaLabel = null,
    string? Title = null,
    string? Icon = null,
    string? IconToggled = null,
    Element? Children = null);

public sealed class PanelVertical : Component<PanelVerticalProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        (VerticalAlignment verticalAlignment, HorizontalAlignment horizontalAlignment, bool isRight) = ResolvePlacement(Props.FabPosition);

        // Mirrors PanelVertical.jsx: `title` is the FAB tooltip only — it never renders as a
        // visible panel header. The expanded panel shows `children` verbatim.
        Element fab = Component<FloatingCircularButton, FloatingCircularButtonProps>(
            new FloatingCircularButtonProps(
                Toggled: Props.Expanded,
                Icon: Props.Icon,
                IconToggled: Props.IconToggled,
                OnClick: () => Props.OnToggle?.Invoke(),
                OnClickToggled: () => Props.OnToggle?.Invoke(),
                AriaLabel: Props.Title ?? Props.AriaLabel ?? "Toggle panel"));

        Element fabHost = Border(fab)
            .Background(theme.SurfaceBackground)
            .CornerRadius(28)
            .Padding(4)
            .HAlign(horizontalAlignment)
            .VAlign(verticalAlignment);

        if (!Props.Expanded)
        {
            return Grid(
                new[] { GridSize.Star() },
                new[] { GridSize.Star() },
                fabHost.Margin(12));
        }

        Element panel = Border(ScrollViewer(Props.Children ?? Empty()).Flex(grow: 1))
            .Padding(12)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(12)
            .Width(480)
            .MaxWidth(560)
            .AutomationName(Props.AriaLabel ?? Props.Title ?? "Panel");

        Element rail = BuildExpandedRail(panel, fabHost, verticalAlignment, horizontalAlignment, isRight);

        return Grid(
            new[] { GridSize.Star() },
            new[] { GridSize.Star() },
            Border(Empty()).Background(BoardTheme.ResolveBrush("BoardOverlayBrush", Color.FromArgb(0x99, 0x06, 0x0C, 0x18))),
            rail.Margin(12));
    }

    private static Element BuildExpandedRail(Element panel, Element fabHost, VerticalAlignment verticalAlignment, HorizontalAlignment horizontalAlignment, bool isRight)
    {
        Element fabColumn = verticalAlignment == VerticalAlignment.Bottom
            ? VStack(Empty().Flex(grow: 1), fabHost)
            : VStack(fabHost, Empty().Flex(grow: 1));

        Element row = isRight
            ? HStack(12, panel.Flex(grow: 0), fabColumn.Flex(grow: 0))
            : HStack(12, fabColumn.Flex(grow: 0), panel.Flex(grow: 0));

        return row
            .HAlign(horizontalAlignment)
            .VAlign(VerticalAlignment.Stretch);
    }

    private static (VerticalAlignment Vertical, HorizontalAlignment Horizontal, bool IsRight) ResolvePlacement(string? fabPosition)
    {
        string[] parts = (fabPosition ?? "top-left").Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
        bool isBottom = parts.Length > 0 && string.Equals(parts[0], "bottom", StringComparison.OrdinalIgnoreCase);
        bool isRight = parts.Length > 1 && string.Equals(parts[1], "right", StringComparison.OrdinalIgnoreCase);
        return (
            isBottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
            isRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            isRight);
    }
}
