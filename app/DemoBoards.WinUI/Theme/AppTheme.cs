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

public sealed record AppThemeTableTokens(
    Brush HeaderBackground,
    Brush HeaderForeground,
    Brush RowStripeBackground,
    Brush GridLine,
    double CellPaddingX,
    double CellPaddingY,
    double HeaderPaddingY,
    double Radius);

public sealed record AppThemeMiniMapTokens(
    Brush NodeFill,
    Brush NodeStroke,
    Brush RunningFill,
    Brush RunningStroke,
    bool RunningPulseEnabled,
    double RunningPulseDurationMs,
    double RunningPulseScaleDelta,
    double RunningOpacityMin,
    double RunningOpacityMax);

public sealed record AppThemeCardRunningTokens(
    Brush Background,
    Brush Border,
    Brush HeaderWash,
    Color GlowColor,
    bool PulseEnabled,
    double PulseDurationMs,
    byte BorderAlphaRest,
    byte BorderAlphaPeak,
    byte HeaderWashAlphaRest,
    byte HeaderWashAlphaPeak);

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
    AppThemeMiniMapTokens MiniMap,
    AppThemeCardRunningTokens RunningCard,
    AppThemeSurfaceTokens Surfaces,
    AppThemeChipTokens Chips,
    AppThemeTableTokens Tables)
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
        MiniMap: new AppThemeMiniMapTokens(
            NodeFill: new SolidColorBrush(Color.FromArgb(0x94, 0x7D, 0x95, 0xAB)),
            NodeStroke: new SolidColorBrush(Color.FromArgb(0x57, 0x61, 0x7A, 0x93)),
            RunningFill: new SolidColorBrush(Color.FromArgb(0xEA, 0x6F, 0xC0, 0x9A)),
            RunningStroke: new SolidColorBrush(Color.FromArgb(0xF5, 0x22, 0x84, 0x5D)),
            RunningPulseEnabled: true,
            RunningPulseDurationMs: 1050,
            RunningPulseScaleDelta: 0.12,
            RunningOpacityMin: 0.72,
            RunningOpacityMax: 1.0),
        RunningCard: new AppThemeCardRunningTokens(
            Background: new SolidColorBrush(Color.FromArgb(0xFF, 0x3E, 0x2C, 0x1E)),
            Border: new SolidColorBrush(Color.FromArgb(0xA8, 0xD4, 0x8A, 0x2A)),
            HeaderWash: new SolidColorBrush(Color.FromArgb(0x24, 0xD4, 0x8A, 0x2A)),
            GlowColor: Color.FromArgb(0xFF, 0xD4, 0x8A, 0x2A),
            PulseEnabled: true,
            PulseDurationMs: 2600,
            BorderAlphaRest: 0x9A,
            BorderAlphaPeak: 0xDA,
            HeaderWashAlphaRest: 0x24,
            HeaderWashAlphaPeak: 0x3C),
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
            CompactRadius: 8),
        Tables: new AppThemeTableTokens(
            HeaderBackground: new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
            HeaderForeground: new SolidColorBrush(Color.FromArgb(0xFF, 0xD8, 0xE0, 0xEA)),
            RowStripeBackground: new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
            GridLine: new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            CellPaddingX: 12,
            CellPaddingY: 8,
            HeaderPaddingY: 9,
            Radius: 10));

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
        Color borderColor = BoardTheme.ResolveColor("BoardColorBorder", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Color borderStrongColor = BoardTheme.ResolveColor("BoardColorBorderStrong", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        Color cardColor = BoardTheme.ResolveColor("BoardColorSurface", Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A));
        Color surfaceStrongColor = BoardTheme.ResolveColor("BoardColorSurfaceStrong", Color.FromArgb(0xFF, 0x2F, 0x2F, 0x2F));

        Color miniMapDefaultFill = Color.FromArgb(0x94, neutralColor.R, neutralColor.G, neutralColor.B);
        Color miniMapDefaultStroke = Color.FromArgb(0x57, neutralColor.R, neutralColor.G, neutralColor.B);
        Color miniMapRunningFill = Color.FromArgb(0xEA, successColor.R, successColor.G, successColor.B);
        Color miniMapRunningStroke = Color.FromArgb(0xF5, successColor.R, successColor.G, successColor.B);
        Color runningCardBackground = Blend(cardColor, runningColor, 0.16);
        Color runningCardBorder = Blend(borderStrongColor, runningColor, 0.56);
        Color runningHeaderWash = Color.FromArgb(0x24, runningColor.R, runningColor.G, runningColor.B);
        Color runningGlow = Blend(surfaceStrongColor, runningColor, 0.82);

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
            MiniMap: new AppThemeMiniMapTokens(
                NodeFill: new SolidColorBrush(miniMapDefaultFill),
                NodeStroke: new SolidColorBrush(miniMapDefaultStroke),
                RunningFill: new SolidColorBrush(miniMapRunningFill),
                RunningStroke: new SolidColorBrush(miniMapRunningStroke),
                RunningPulseEnabled: true,
                RunningPulseDurationMs: 1050,
                RunningPulseScaleDelta: 0.12,
                RunningOpacityMin: 0.72,
                RunningOpacityMax: 1.0),
            RunningCard: new AppThemeCardRunningTokens(
                Background: new SolidColorBrush(runningCardBackground),
                Border: new SolidColorBrush(runningCardBorder),
                HeaderWash: new SolidColorBrush(runningHeaderWash),
                GlowColor: runningGlow,
                PulseEnabled: true,
                PulseDurationMs: 2600,
                BorderAlphaRest: 0x9A,
                BorderAlphaPeak: 0xDA,
                HeaderWashAlphaRest: 0x24,
                HeaderWashAlphaPeak: 0x3C),
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
                CompactRadius: 8),
            Tables: new AppThemeTableTokens(
                HeaderBackground: ResolveBrush("BoardTableHeaderBackgroundBrush", Color.FromArgb(0x20, borderStrongColor.R, borderStrongColor.G, borderStrongColor.B)),
                HeaderForeground: ResolveBrush("BoardTableHeaderForegroundBrush", textMuted is SolidColorBrush mutedBrush ? mutedBrush.Color : Color.FromArgb(0xFF, 0xB8, 0xC0, 0xCC)),
                RowStripeBackground: ResolveBrush("BoardTableRowStripeBackgroundBrush", Color.FromArgb(0x10, borderColor.R, borderColor.G, borderColor.B)),
                GridLine: ResolveBrush("BoardTableGridLineBrush", borderColor),
                CellPaddingX: 12,
                CellPaddingY: 8,
                HeaderPaddingY: 9,
                Radius: 10));
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

    public Brush MiniMapFillForTone(string? tone)
    {
        return NormalizeTone(tone) switch
        {
            "running" => MiniMap.RunningFill,
            "success" or "completed" => new SolidColorBrush(Color.FromArgb(0xEA, StatusSuccessColor.R, StatusSuccessColor.G, StatusSuccessColor.B)),
            "failed" or "error" => new SolidColorBrush(Color.FromArgb(0xEA, StatusErrorColor.R, StatusErrorColor.G, StatusErrorColor.B)),
            "warning" or "blocked" => new SolidColorBrush(Color.FromArgb(0xEA, StatusWarningColor.R, StatusWarningColor.G, StatusWarningColor.B)),
            _ => MiniMap.NodeFill,
        };
    }

    public Brush MiniMapStrokeForTone(string? tone)
    {
        return NormalizeTone(tone) switch
        {
            "running" => MiniMap.RunningStroke,
            "success" or "completed" => new SolidColorBrush(Color.FromArgb(0xF5, StatusSuccessColor.R, StatusSuccessColor.G, StatusSuccessColor.B)),
            "failed" or "error" => new SolidColorBrush(Color.FromArgb(0xF5, StatusErrorColor.R, StatusErrorColor.G, StatusErrorColor.B)),
            "warning" or "blocked" => new SolidColorBrush(Color.FromArgb(0xF5, StatusWarningColor.R, StatusWarningColor.G, StatusWarningColor.B)),
            _ => MiniMap.NodeStroke,
        };
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

    private static string NormalizeTone(string? tone) =>
        (tone ?? string.Empty).Trim().ToLowerInvariant();

    private static Color Blend(Color background, Color foreground, double amount)
    {
        double clamped = Math.Clamp(amount, 0, 1);
        byte Mix(byte lhs, byte rhs) => (byte)Math.Round(lhs + ((rhs - lhs) * clamped));
        return Color.FromArgb(
            Mix(background.A, foreground.A),
            Mix(background.R, foreground.R),
            Mix(background.G, foreground.G),
            Mix(background.B, foreground.B));
    }
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
