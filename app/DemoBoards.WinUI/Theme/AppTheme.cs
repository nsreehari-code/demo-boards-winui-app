using Microsoft.UI;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DemoBoards_WinUI;

/// <summary>
/// The app theme as a small set of <b>semantic</b> brushes/colours that Reactor components consume
/// through <see cref="AppThemeContext"/> instead of reaching into XAML resources directly. Each role
/// maps to a Fluent theme resource (resolved live from <see cref="Application.Current"/>'s merged
/// dictionaries, which the active <c>BoardTheme</c> pack swaps), so changing the theme pack flows
/// through automatically. Brush members are the cached resource instances, so two themes resolved
/// from the same pack are record-equal — the context only invalidates consumers when the pack changes.
/// </summary>
public sealed record AppTheme(
    Brush SurfaceBackground,
    Brush CardBackground,
    Brush CardBorder,
    Brush Accent,
    Brush Layer,
    Brush LayerAlt,
    Brush TextPrimary,
    Color GridDotColor,
    Color MiniMapViewportFill)
{
    /// <summary>
    /// Resource-independent neutral theme used as the <see cref="AppThemeContext"/> default (only seen
    /// if a consumer renders with no provider above it). The real values come from <see cref="FromResources"/>.
    /// </summary>
    public static readonly AppTheme Fallback = new(
        SurfaceBackground: new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
        CardBackground: new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A)),
        CardBorder: new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        Accent: new SolidColorBrush(Color.FromArgb(0xFF, 0x30, 0x90, 0xF0)),
        Layer: new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
        LayerAlt: new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
        TextPrimary: new SolidColorBrush(Colors.White),
        GridDotColor: Color.FromArgb(0x22, 0x88, 0x88, 0x88),
        MiniMapViewportFill: Color.FromArgb(0x33, 0x30, 0x90, 0xF0));

    /// <summary>
    /// Builds the live theme from the application's current Fluent/board theme resources. The two
    /// canvas-only colours (grid dot, minimap viewport fill) are derived from the resolved text/accent
    /// colours so they track the active pack too.
    /// </summary>
    public static AppTheme FromResources()
    {
        Brush accent = ResolveBrush("AccentFillColorDefaultBrush", Color.FromArgb(0xFF, 0x30, 0x90, 0xF0));
        Brush text = ResolveBrush("TextFillColorPrimaryBrush", Colors.White);

        Color accentColor = accent is SolidColorBrush ab ? ab.Color : Color.FromArgb(0xFF, 0x30, 0x90, 0xF0);
        Color textColor = text is SolidColorBrush tb ? tb.Color : Color.FromArgb(0xFF, 0x88, 0x88, 0x88);

        return new AppTheme(
            SurfaceBackground: ResolveBrush("SolidBackgroundFillColorBaseAltBrush", Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
            CardBackground: ResolveBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A)),
            CardBorder: ResolveBrush("CardStrokeColorDefaultBrush", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Accent: accent,
            Layer: ResolveBrush("LayerFillColorDefaultBrush", Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            LayerAlt: ResolveBrush("LayerFillColorAltBrush", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            TextPrimary: text,
            GridDotColor: Color.FromArgb(0x22, textColor.R, textColor.G, textColor.B),
            MiniMapViewportFill: Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B));
    }

    private static Brush ResolveBrush(string resourceKey, Color fallback) =>
        Application.Current.Resources.TryGetValue(resourceKey, out object resource) && resource is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}

/// <summary>
/// Tree-scoped provider for <see cref="AppTheme"/>. A parent supplies the live theme with
/// <c>element.Provide(AppThemeContext.Current, AppTheme.FromResources())</c>; any descendant reads it
/// with <c>UseContext(AppThemeContext.Current)</c> — no prop drilling and no static resource lookups
/// inside the consuming component.
/// </summary>
public static class AppThemeContext
{
    public static readonly Context<AppTheme> Current = new(AppTheme.Fallback);
}
