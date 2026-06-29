using Microsoft.UI;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DemoBoards_WinUI;

public sealed record AppThemeSurfaceTokens(
    double CardPadding,
    double CardRadius,
    double TilePadding,
    double TileRadius,
    double PanelPadding,
    double PanelRadius,
    double DialogPadding,
    double DialogRadius,
    double BubblePadding,
    double BubbleRadius);

public sealed record AppThemeChipTokens(
    double PaddingX,
    double PaddingY,
    double Radius,
    double CompactPaddingX,
    double CompactPaddingY,
    double CompactRadius);

/// <summary>
/// The app theme as a small set of <b>semantic</b> brushes/colours that Reactor components consume
/// through <see cref="AppThemeContext"/> instead of reaching into XAML resources directly. Each role
/// maps to a Fluent theme resource (resolved live from <see cref="Application.Current"/>'s merged
/// dictionaries, which the active <c>BoardTheme</c> pack swaps), so changing the theme pack flows
/// through automatically. Brush members are the cached resource instances, so two themes resolved
/// from the same pack are record-equal — the context only invalidates consumers when the pack changes.
/// </summary>
public sealed record AppTheme(
    Brush Transparent,
    Brush WindowBackground,
    Brush TopBarBackground,
    Brush TopBarBorder,
    Brush SecondaryCardBackground,
    Brush SurfaceBackground,
    Brush SurfaceElevated,
    Brush CardBackground,
    Brush CardBorder,
    Brush CardBorderStrong,
    Brush Accent,
    Brush AccentStrong,
    Brush AccentSoft,
    Brush ControlFill,
    Brush Overlay,
    Brush Layer,
    Brush LayerAlt,
    Brush TextPrimary,
    Brush TextMuted,
    Brush TextSoft,
    Brush TextOnAccent,
    Brush StatusRunning,
    Brush StatusSuccess,
    Brush StatusError,
    Brush StatusWarning,
    Brush StatusNeutral,
    Color StatusRunningColor,
    Color StatusSuccessColor,
    Color StatusErrorColor,
    Color StatusWarningColor,
    Color StatusNeutralColor,
    Color GridDotColor,
    Color MiniMapViewportFill,
    AppThemeSurfaceTokens Surfaces,
    AppThemeChipTokens Chips)
{
    /// <summary>
    /// Resource-independent neutral theme used as the <see cref="AppThemeContext"/> default (only seen
    /// if a consumer renders with no provider above it). The real values come from <see cref="FromResources"/>.
    /// </summary>
    public static readonly AppTheme Fallback = new(
        Transparent: new SolidColorBrush(Colors.Transparent),
        WindowBackground: new SolidColorBrush(Color.FromArgb(0xFF, 0x18, 0x18, 0x18)),
        TopBarBackground: new SolidColorBrush(Color.FromArgb(0xFF, 0x24, 0x24, 0x24)),
        TopBarBorder: new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        SecondaryCardBackground: new SolidColorBrush(Color.FromArgb(0xFF, 0x24, 0x24, 0x24)),
        SurfaceBackground: new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
        SurfaceElevated: new SolidColorBrush(Color.FromArgb(0xFF, 0x2F, 0x2F, 0x2F)),
        CardBackground: new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A)),
        CardBorder: new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        CardBorderStrong: new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
        Accent: new SolidColorBrush(Color.FromArgb(0xFF, 0x30, 0x90, 0xF0)),
        AccentStrong: new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x72, 0xC8)),
        AccentSoft: new SolidColorBrush(Color.FromArgb(0x26, 0x30, 0x90, 0xF0)),
        ControlFill: new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
        Overlay: new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
        Layer: new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
        LayerAlt: new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
        TextPrimary: new SolidColorBrush(Colors.White),
        TextMuted: new SolidColorBrush(Color.FromArgb(0xFF, 0x99, 0x99, 0x99)),
        TextSoft: new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77)),
        TextOnAccent: new SolidColorBrush(Colors.White),
        StatusRunning: new SolidColorBrush(Color.FromArgb(0xFF, 0x30, 0x90, 0xF0)),
        StatusSuccess: new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0xB5, 0x7D)),
        StatusError: new SolidColorBrush(Color.FromArgb(0xFF, 0xD6, 0x45, 0x5D)),
        StatusWarning: new SolidColorBrush(Color.FromArgb(0xFF, 0xD9, 0x92, 0x2E)),
        StatusNeutral: new SolidColorBrush(Color.FromArgb(0xFF, 0x81, 0x91, 0xA3)),
        StatusRunningColor: Color.FromArgb(0xFF, 0x30, 0x90, 0xF0),
        StatusSuccessColor: Color.FromArgb(0xFF, 0x2E, 0xB5, 0x7D),
        StatusErrorColor: Color.FromArgb(0xFF, 0xD6, 0x45, 0x5D),
        StatusWarningColor: Color.FromArgb(0xFF, 0xD9, 0x92, 0x2E),
        StatusNeutralColor: Color.FromArgb(0xFF, 0x81, 0x91, 0xA3),
        GridDotColor: Color.FromArgb(0x22, 0x88, 0x88, 0x88),
        MiniMapViewportFill: Color.FromArgb(0x33, 0x30, 0x90, 0xF0),
        Surfaces: new AppThemeSurfaceTokens(
            CardPadding: 8,
            CardRadius: 4,
            TilePadding: 12,
            TileRadius: 10,
            PanelPadding: 12,
            PanelRadius: 2,
            DialogPadding: 16,
            DialogRadius: 16,
            BubblePadding: 10,
            BubbleRadius: 12),
        Chips: new AppThemeChipTokens(
            PaddingX: 8,
            PaddingY: 2,
            Radius: 10,
            CompactPaddingX: 6,
            CompactPaddingY: 2,
            CompactRadius: 8));

    /// <summary>
    /// Builds the live theme from the application's current Fluent/board theme resources. The two
    /// canvas-only colours (grid dot, minimap viewport fill) are derived from the resolved text/accent
    /// colours so they track the active pack too.
    /// </summary>
    public static AppTheme FromResources()
    {
        Brush accent = ResolveBrush("AccentFillColorDefaultBrush", Color.FromArgb(0xFF, 0x30, 0x90, 0xF0));
        Brush accentStrong = ResolveBrush("BoardAccentStrongBrush", Color.FromArgb(0xFF, 0x17, 0x6D, 0xD1));
        Brush accentSoft = ResolveBrush("BoardAccentSoftBrush", Color.FromArgb(0x1F, 0x1F, 0x8F, 0xFF));
        Brush text = ResolveBrush("TextFillColorPrimaryBrush", Colors.White);
        Brush textMuted = ResolveBrush("BoardTextMutedBrush", Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
        Brush textSoft = ResolveBrush("BoardTextSoftBrush", Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
        Brush textOnAccent = ResolveBrush("TextOnAccentFillColorPrimaryBrush", Colors.White);
        Brush statusRunning = ResolveBrush("BoardStatusRunningBrush", BoardTheme.ResolveColor("BoardStatusRunningColor", Color.FromArgb(0xFF, 0xD4, 0x8A, 0x2A)));
        Brush statusSuccess = ResolveBrush("BoardStatusCompletedBrush", BoardTheme.ResolveColor("BoardStatusCompletedColor", Color.FromArgb(0xFF, 0x18, 0xB5, 0x7D)));
        Brush statusError = ResolveBrush("BoardStatusFailedBrush", BoardTheme.ResolveColor("BoardStatusFailedColor", Color.FromArgb(0xFF, 0xD6, 0x45, 0x5D)));
        Brush statusWarning = ResolveBrush("BoardStatusBlockedBrush", BoardTheme.ResolveColor("BoardStatusBlockedColor", Color.FromArgb(0xFF, 0xD9, 0x92, 0x2E)));
        Brush statusNeutral = ResolveBrush("BoardStatusUnknownBrush", BoardTheme.ResolveColor("BoardStatusUnknownColor", Color.FromArgb(0xFF, 0x81, 0x91, 0xA3)));

        Color accentColor = accent is SolidColorBrush ab ? ab.Color : Color.FromArgb(0xFF, 0x30, 0x90, 0xF0);
        Color textColor = text is SolidColorBrush tb ? tb.Color : Color.FromArgb(0xFF, 0x88, 0x88, 0x88);
        Color runningColor = BoardTheme.ResolveColor("BoardStatusRunningColor", accentColor);
        Color successColor = BoardTheme.ResolveColor("BoardStatusCompletedColor", Color.FromArgb(0xFF, 0x18, 0xB5, 0x7D));
        Color errorColor = BoardTheme.ResolveColor("BoardStatusFailedColor", Color.FromArgb(0xFF, 0xD6, 0x45, 0x5D));
        Color warningColor = BoardTheme.ResolveColor("BoardStatusBlockedColor", Color.FromArgb(0xFF, 0xD9, 0x92, 0x2E));
        Color neutralColor = BoardTheme.ResolveColor("BoardStatusUnknownColor", Color.FromArgb(0xFF, 0x81, 0x91, 0xA3));

        return new AppTheme(
            Transparent: new SolidColorBrush(Colors.Transparent),
            WindowBackground: ResolveBrush("BoardWindowBackgroundBrush", Color.FromArgb(0xFF, 0x18, 0x18, 0x18)),
            TopBarBackground: ResolveBrush("BoardTopBarBackgroundBrush", Color.FromArgb(0xFF, 0x24, 0x24, 0x24)),
            TopBarBorder: ResolveBrush("BoardTopBarBorderBrush", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            SecondaryCardBackground: ResolveBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(0xFF, 0x24, 0x24, 0x24)),
            SurfaceBackground: ResolveBrush("SolidBackgroundFillColorBaseAltBrush", Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
            SurfaceElevated: ResolveBrush("BoardSurfaceStrongBrush", Color.FromArgb(0xFF, 0x2F, 0x2F, 0x2F)),
            CardBackground: ResolveBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A)),
            CardBorder: ResolveBrush("CardStrokeColorDefaultBrush", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            CardBorderStrong: ResolveBrush("BoardBorderStrongBrush", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            Accent: accent,
            AccentStrong: accentStrong,
            AccentSoft: accentSoft,
            ControlFill: ResolveBrush("ControlFillColorDefaultBrush", Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            Overlay: ResolveBrush("BoardOverlayBrush", Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
            Layer: ResolveBrush("LayerFillColorDefaultBrush", Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            LayerAlt: ResolveBrush("LayerFillColorAltBrush", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            TextPrimary: text,
            TextMuted: textMuted,
            TextSoft: textSoft,
            TextOnAccent: textOnAccent,
            StatusRunning: statusRunning,
            StatusSuccess: statusSuccess,
            StatusError: statusError,
            StatusWarning: statusWarning,
            StatusNeutral: statusNeutral,
            StatusRunningColor: runningColor,
            StatusSuccessColor: successColor,
            StatusErrorColor: errorColor,
            StatusWarningColor: warningColor,
            StatusNeutralColor: neutralColor,
            GridDotColor: Color.FromArgb(0x22, textColor.R, textColor.G, textColor.B),
            MiniMapViewportFill: Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B),
            Surfaces: new AppThemeSurfaceTokens(
                CardPadding: 8,
                CardRadius: 4,
                TilePadding: 12,
                TileRadius: 10,
                PanelPadding: 12,
                PanelRadius: 2,
                DialogPadding: 16,
                DialogRadius: 16,
                BubblePadding: 10,
                BubbleRadius: 12),
            Chips: new AppThemeChipTokens(
                PaddingX: 8,
                PaddingY: 2,
                Radius: 10,
                CompactPaddingX: 6,
                CompactPaddingY: 2,
                CompactRadius: 8));
    }

    public Brush BrushForTone(string? tone)
    {
        return (tone ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "green" or "success" or "completed" => StatusSuccess,
            "amber" or "warning" or "blocked" or "cancelled" => StatusWarning,
            "red" or "danger" or "failed" or "error" => StatusError,
            "blue" or "primary" or "running" or "in_progress" => StatusRunning,
            "secondary" or "unknown" or "" => StatusNeutral,
            _ => StatusNeutral,
        };
    }

    public Color ColorForTone(string? tone)
    {
        return (tone ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "green" or "success" or "completed" => StatusSuccessColor,
            "amber" or "warning" or "blocked" or "cancelled" => StatusWarningColor,
            "red" or "danger" or "failed" or "error" => StatusErrorColor,
            "blue" or "primary" or "running" or "in_progress" => StatusRunningColor,
            "secondary" or "unknown" or "" => StatusNeutralColor,
            _ => StatusNeutralColor,
        };
    }

    public Brush SurfaceForTone(string? tone, byte alpha = 0x1F)
    {
        Color color = ColorForTone(tone);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public IReadOnlyList<Brush> CreateChartPalette()
    {
        return new Brush[]
        {
            AccentStrong,
            StatusWarning,
            StatusError,
            StatusRunning,
            StatusSuccess,
            Accent,
            TextMuted,
            CardBorderStrong,
            AccentSoft,
            TextSoft,
        };
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
