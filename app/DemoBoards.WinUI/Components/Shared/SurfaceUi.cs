using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Thin semantic surface builders for the repeated shell roles used across the app. Components still
/// own their structure; these helpers only apply shared theme-backed geometry and chrome.
/// </summary>
public static class SurfaceUi
{
    public static Element CardSurface(AppTheme theme, Element child) =>
        Border(child)
            .Padding(theme.Surfaces.CardPadding)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(theme.Surfaces.CardRadius);

    public static Element TileSurface(AppTheme theme, Element child) =>
        Border(child)
            .Padding(theme.Surfaces.TilePadding)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(theme.Surfaces.TileRadius);

    public static Element PanelSurface(AppTheme theme, Element child) =>
        Border(child)
            .Padding(theme.Surfaces.PanelPadding)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(theme.Surfaces.PanelRadius);

    public static Element DialogSurface(AppTheme theme, Element child) =>
        Border(child)
            .Padding(theme.Surfaces.DialogPadding)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(theme.Surfaces.DialogRadius);

    public static Element BubbleSurface(AppTheme theme, Element child, Brush background, Action<Border>? configure = null) =>
        Border(child)
            .Padding(theme.Surfaces.BubblePadding)
            .Background(background)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(theme.Surfaces.BubbleRadius)
            .Set(border => configure?.Invoke(border));

    public static Element ChipSurface(AppTheme theme, Element child, Brush background) =>
        Border(child)
            .Padding(theme.Chips.PaddingX, theme.Chips.PaddingY, theme.Chips.PaddingX, theme.Chips.PaddingY)
            .Background(background)
            .CornerRadius(theme.Chips.Radius);
}