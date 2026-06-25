using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace DemoBoards_WinUI;

internal static class BoardTheme
{
    public const string DefaultThemePackId = "mist-ops";
    public const string SignalRoomThemePackId = "signal-room";

    public static readonly string[] ThemePackIds = [DefaultThemePackId, SignalRoomThemePackId];

    public static string NormalizeThemePackId(string? themePackId)
    {
        string normalized = themePackId?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized == SignalRoomThemePackId ? SignalRoomThemePackId : DefaultThemePackId;
    }

    public static string ResolveThemePackIdFromUiJson(string? rawUiJson)
    {
        if (string.IsNullOrWhiteSpace(rawUiJson))
        {
            return DefaultThemePackId;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawUiJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("theme", out JsonElement theme)
                && theme.ValueKind == JsonValueKind.Object
                && theme.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String)
            {
                return NormalizeThemePackId(id.GetString());
            }
        }
        catch
        {
        }

        return DefaultThemePackId;
    }

    public static ResourceDictionary CreateThemeDictionary(string? themePackId)
    {
        string normalized = NormalizeThemePackId(themePackId);
        var palette = normalized == SignalRoomThemePackId ? CreateSignalRoomPalette() : CreateMistOpsPalette();
        var dictionary = new ResourceDictionary();

        AddColor(dictionary, "BoardColorBg", palette.BoardColorBg);
        AddColor(dictionary, "BoardColorBgElevated", palette.BoardColorBgElevated);
        AddColor(dictionary, "BoardColorSurface", palette.BoardColorSurface);
        AddColor(dictionary, "BoardColorSurfaceStrong", palette.BoardColorSurfaceStrong);
        AddColor(dictionary, "BoardColorSurfaceMuted", palette.BoardColorSurfaceMuted);
        AddColor(dictionary, "BoardColorBorder", palette.BoardColorBorder);
        AddColor(dictionary, "BoardColorBorderStrong", palette.BoardColorBorderStrong);
        AddColor(dictionary, "BoardColorText", palette.BoardColorText);
        AddColor(dictionary, "BoardColorTextMuted", palette.BoardColorTextMuted);
        AddColor(dictionary, "BoardColorTextSoft", palette.BoardColorTextSoft);
        AddColor(dictionary, "BoardColorAccent", palette.BoardColorAccent);
        AddColor(dictionary, "BoardColorAccentStrong", palette.BoardColorAccentStrong);
        AddColor(dictionary, "BoardColorAccentSoft", palette.BoardColorAccentSoft);
        AddColor(dictionary, "BoardColorOverlay", palette.BoardColorOverlay);
        AddColor(dictionary, "BoardStatusRunningColor", palette.BoardStatusRunningColor);
        AddColor(dictionary, "BoardStatusCompletedColor", palette.BoardStatusCompletedColor);
        AddColor(dictionary, "BoardStatusFailedColor", palette.BoardStatusFailedColor);
        AddColor(dictionary, "BoardStatusBlockedColor", palette.BoardStatusBlockedColor);
        AddColor(dictionary, "BoardStatusFreshColor", palette.BoardStatusFreshColor);
        AddColor(dictionary, "BoardStatusUnknownColor", palette.BoardStatusUnknownColor);

        dictionary["BoardWindowBackgroundBrush"] = CreateVerticalGradientBrush(palette.BoardWindowBackgroundGradientStart, palette.BoardWindowBackgroundGradientMid, palette.BoardWindowBackgroundGradientEnd, palette.BoardWindowBackgroundMidOffset);
        dictionary["BoardTopBarBackgroundBrush"] = CreateDiagonalGradientBrush(palette.BoardTopBarBackgroundGradientStart, palette.BoardTopBarBackgroundGradientEnd);
        dictionary["BoardTopBarBorderBrush"] = new SolidColorBrush(palette.BoardColorBorderStrong);
        dictionary["BoardTextBrush"] = new SolidColorBrush(palette.BoardColorText);
        dictionary["BoardTextMutedBrush"] = new SolidColorBrush(palette.BoardColorTextMuted);
        dictionary["BoardTextSoftBrush"] = new SolidColorBrush(palette.BoardColorTextSoft);
        dictionary["BoardBorderBrush"] = new SolidColorBrush(palette.BoardColorBorder);
        dictionary["BoardBorderStrongBrush"] = new SolidColorBrush(palette.BoardColorBorderStrong);
        dictionary["BoardAccentBrush"] = new SolidColorBrush(palette.BoardColorAccent);
        dictionary["BoardAccentStrongBrush"] = new SolidColorBrush(palette.BoardColorAccentStrong);
        dictionary["BoardAccentSoftBrush"] = new SolidColorBrush(palette.BoardColorAccentSoft);
        dictionary["BoardOverlayBrush"] = new SolidColorBrush(palette.BoardColorOverlay);
        dictionary["BoardSurfaceStrongBrush"] = new SolidColorBrush(palette.BoardColorSurfaceStrong);
        dictionary["BoardSurfaceMutedBrush"] = new SolidColorBrush(palette.BoardColorSurfaceMuted);
        dictionary["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(palette.BoardColorSurface);
        dictionary["LayerOnAcrylicFillColorDefaultBrush"] = new SolidColorBrush(palette.BoardColorSurfaceStrong);
        dictionary["BoardCanvasPanelBrush"] = new SolidColorBrush(palette.BoardCanvasPanelColor);
        dictionary["BoardCanvasPanelBorderBrush"] = new SolidColorBrush(palette.BoardCanvasPanelBorderColor);
        dictionary["BoardCanvasMiniMapBrush"] = new SolidColorBrush(palette.BoardCanvasMiniMapColor);
        dictionary["BoardCanvasMiniMapBorderBrush"] = new SolidColorBrush(palette.BoardCanvasMiniMapBorderColor);
        dictionary["BoardCanvasMiniMapViewportBrush"] = new SolidColorBrush(palette.BoardCanvasMiniMapViewportColor);
        dictionary["BoardCanvasMiniMapViewportBorderBrush"] = new SolidColorBrush(palette.BoardCanvasMiniMapViewportBorderColor);
        dictionary["BoardToolbarButtonBackgroundBrush"] = new SolidColorBrush(palette.BoardToolbarButtonBackgroundColor);
        dictionary["BoardToolbarButtonForegroundBrush"] = new SolidColorBrush(palette.BoardToolbarButtonForegroundColor);
        dictionary["BoardToolbarButtonBorderBrush"] = new SolidColorBrush(palette.BoardToolbarButtonBorderColor);
        dictionary["BoardFloatingButtonBackgroundBrush"] = new SolidColorBrush(palette.BoardFloatingButtonBackgroundColor);
        dictionary["BoardFloatingButtonForegroundBrush"] = new SolidColorBrush(palette.BoardFloatingButtonForegroundColor);
        dictionary["BoardFloatingButtonBorderBrush"] = new SolidColorBrush(palette.BoardFloatingButtonBorderColor);
        dictionary["BoardFloatingButtonActiveBackgroundBrush"] = new SolidColorBrush(palette.BoardFloatingButtonActiveBackgroundColor);
        dictionary["BoardFloatingButtonActiveForegroundBrush"] = new SolidColorBrush(palette.BoardFloatingButtonActiveForegroundColor);
        dictionary["BoardFloatingButtonActiveBorderBrush"] = new SolidColorBrush(palette.BoardFloatingButtonActiveBorderColor);
        dictionary["BoardEdgeToggleButtonBackgroundBrush"] = new SolidColorBrush(palette.BoardEdgeToggleButtonBackgroundColor);
        dictionary["BoardEdgeToggleButtonForegroundBrush"] = new SolidColorBrush(palette.BoardEdgeToggleButtonForegroundColor);
        dictionary["BoardEdgeToggleButtonBorderBrush"] = new SolidColorBrush(palette.BoardEdgeToggleButtonBorderColor);
        dictionary["BoardEdgeToggleButtonActiveBackgroundBrush"] = new SolidColorBrush(palette.BoardEdgeToggleButtonActiveBackgroundColor);
        dictionary["BoardEdgeToggleButtonActiveForegroundBrush"] = new SolidColorBrush(palette.BoardEdgeToggleButtonActiveForegroundColor);
        dictionary["BoardEdgeToggleButtonActiveBorderBrush"] = new SolidColorBrush(palette.BoardEdgeToggleButtonActiveBorderColor);

        Style floatingStyle = CreateFloatingCircleButtonStyle(dictionary);
        dictionary["BoardToolbarButtonStyle"] = CreateToolbarButtonStyle(dictionary);
        dictionary["BoardFloatingCircleButtonStyle"] = floatingStyle;
        dictionary["BoardFloatingCircleButtonActiveStyle"] = CreateFloatingCircleButtonActiveStyle(dictionary, floatingStyle);
        Style edgeToggleStyle = CreateEdgeToggleButtonStyle(dictionary, floatingStyle);
        dictionary["BoardEdgeToggleButtonStyle"] = edgeToggleStyle;
        dictionary["BoardEdgeToggleButtonActiveStyle"] = CreateEdgeToggleButtonActiveStyle(dictionary, edgeToggleStyle);

        return dictionary;
    }

    public static Brush CreateStatusBrush(string? status, byte alpha)
    {
        string resourceKey = NormalizeStatus(status) switch
        {
            "completed" => "BoardStatusCompletedColor",
            "running" => "BoardStatusRunningColor",
            "in_progress" => "BoardStatusRunningColor",
            "failed" => "BoardStatusFailedColor",
            "blocked" => "BoardStatusBlockedColor",
            "fresh" => "BoardStatusFreshColor",
            _ => "BoardStatusUnknownColor"
        };

        Color color = ResolveColor(resourceKey, Color.FromArgb(0xFF, 0x70, 0x80, 0x90));
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public static Brush CreateResourceBrush(string resourceKey, byte alpha, Color fallback)
    {
        Color color = ResolveColor(resourceKey, fallback);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public static Brush ResolveBrush(string resourceKey, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(resourceKey, out object? resource) == true)
        {
            if (resource is Brush brush)
            {
                return brush;
            }

            if (resource is Color color)
            {
                return new SolidColorBrush(color);
            }
        }

        return new SolidColorBrush(fallback);
    }

    public static Color ResolveColor(string resourceKey, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(resourceKey, out object? resource) == true)
        {
            if (resource is Color color)
            {
                return color;
            }

            if (resource is SolidColorBrush brush)
            {
                return brush.Color;
            }
        }

        return fallback;
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static void AddColor(ResourceDictionary dictionary, string key, Color color)
    {
        dictionary[key] = color;
    }

    private static LinearGradientBrush CreateVerticalGradientBrush(Color start, Color mid, Color end, double midOffset)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = start, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = mid, Offset = midOffset });
        brush.GradientStops.Add(new GradientStop { Color = end, Offset = 1 });
        return brush;
    }

    private static LinearGradientBrush CreateDiagonalGradientBrush(Color start, Color end)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = start, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = end, Offset = 1 });
        return brush;
    }

    private static Style CreateToolbarButtonStyle(ResourceDictionary dictionary)
    {
        var style = new Style { TargetType = typeof(Button) };
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 3, 8, 3)));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 28d));
        style.Setters.Add(new Setter(Control.BackgroundProperty, dictionary["BoardToolbarButtonBackgroundBrush"]));
        style.Setters.Add(new Setter(Control.ForegroundProperty, dictionary["BoardToolbarButtonForegroundBrush"]));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, dictionary["BoardToolbarButtonBorderBrush"]));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        return style;
    }

    private static Style CreateFloatingCircleButtonStyle(ResourceDictionary dictionary)
    {
        var style = new Style { TargetType = typeof(Button) };
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, dictionary["BoardFloatingButtonBackgroundBrush"]));
        style.Setters.Add(new Setter(Control.ForegroundProperty, dictionary["BoardFloatingButtonForegroundBrush"]));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, dictionary["BoardFloatingButtonBorderBrush"]));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 42d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 42d));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    private static Style CreateFloatingCircleButtonActiveStyle(ResourceDictionary dictionary, Style basedOn)
    {
        var style = new Style { TargetType = typeof(Button), BasedOn = basedOn };
        style.Setters.Add(new Setter(Control.BackgroundProperty, dictionary["BoardFloatingButtonActiveBackgroundBrush"]));
        style.Setters.Add(new Setter(Control.ForegroundProperty, dictionary["BoardFloatingButtonActiveForegroundBrush"]));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, dictionary["BoardFloatingButtonActiveBorderBrush"]));
        return style;
    }

    private static Style CreateEdgeToggleButtonStyle(ResourceDictionary dictionary, Style basedOn)
    {
        var style = new Style { TargetType = typeof(Button), BasedOn = basedOn };
        style.Setters.Add(new Setter(Control.BackgroundProperty, dictionary["BoardEdgeToggleButtonBackgroundBrush"]));
        style.Setters.Add(new Setter(Control.ForegroundProperty, dictionary["BoardEdgeToggleButtonForegroundBrush"]));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, dictionary["BoardEdgeToggleButtonBorderBrush"]));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 48d));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 48d));
        return style;
    }

    private static Style CreateEdgeToggleButtonActiveStyle(ResourceDictionary dictionary, Style basedOn)
    {
        var style = new Style { TargetType = typeof(Button), BasedOn = basedOn };
        style.Setters.Add(new Setter(Control.BackgroundProperty, dictionary["BoardEdgeToggleButtonActiveBackgroundBrush"]));
        style.Setters.Add(new Setter(Control.ForegroundProperty, dictionary["BoardEdgeToggleButtonActiveForegroundBrush"]));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, dictionary["BoardEdgeToggleButtonActiveBorderBrush"]));
        return style;
    }

    private static ThemePalette CreateMistOpsPalette()
    {
        return new ThemePalette(
            ParseColor("#FFE4EBF2"), ParseColor("#FFDBE4EC"), ParseColor("#F0F9FBFD"), ParseColor("#FAF5F8FB"), ParseColor("#EBE8EEF4"),
            ParseColor("#2E476586"), ParseColor("#473877C2"), ParseColor("#FF16324A"), ParseColor("#FF5F768E"), ParseColor("#FF7A8EA4"),
            ParseColor("#FF1F8FFF"), ParseColor("#FF176DD1"), ParseColor("#1F1F8FFF"), ParseColor("#33142A41"), ParseColor("#FFD48A2A"),
            ParseColor("#FF18B57D"), ParseColor("#FFD6455D"), ParseColor("#FFD9922E"), ParseColor("#FF6586AA"), ParseColor("#FF8191A3"),
            ParseColor("#FFEDF3F8"), ParseColor("#FFE3EBF2"), ParseColor("#FFDDE6EE"), 0.48,
            ParseColor("#FAEEF4F9"), ParseColor("#F0E0E9F1"), ParseColor("#EEF5F8FB"), ParseColor("#66476586"), ParseColor("#F2F9FBFD"), ParseColor("#66476586"),
            ParseColor("#1F1F8FFF"), ParseColor("#CC176DD1"), ParseColor("#00FFFFFF"), ParseColor("#FF16324A"), ParseColor("#473877C2"), ParseColor("#F7F7FBFE"),
            ParseColor("#FF16324A"), ParseColor("#7A1F8FFF"), ParseColor("#F5E1EAF0"), ParseColor("#FF0C375F"), ParseColor("#7A4B739C"), ParseColor("#F6D8E2EA"),
            ParseColor("#FF176DD1"), ParseColor("#5A3D6C98"), ParseColor("#F7E7EEF4"), ParseColor("#FF0E5AAF"), ParseColor("#8A3D6C98"));
    }

    private static ThemePalette CreateSignalRoomPalette()
    {
        return new ThemePalette(
            ParseColor("#FF08111F"), ParseColor("#FF0D1728"), ParseColor("#EB111B2E"), ParseColor("#FA152238"), ParseColor("#D10E1829"),
            ParseColor("#2E6E9AD7"), ParseColor("#577AADFF"), ParseColor("#FFE5EEFC"), ParseColor("#FF94A9C6"), ParseColor("#FF7387A2"),
            ParseColor("#FF54A6FF"), ParseColor("#FF7DC7FF"), ParseColor("#2954A6FF"), ParseColor("#C2040A13"), ParseColor("#FFF2B462"),
            ParseColor("#FF2ED1A2"), ParseColor("#FFFF6B81"), ParseColor("#FFF2B84B"), ParseColor("#FF8EA6C4"), ParseColor("#FF7D8FA8"),
            ParseColor("#FF09111F"), ParseColor("#FF07101B"), ParseColor("#FF060D17"), 0.52,
            ParseColor("#F00D1728"), ParseColor("#E009121F"), ParseColor("#F0152238"), ParseColor("#666E9AD7"), ParseColor("#F2111B2E"), ParseColor("#666E9AD7"),
            ParseColor("#2954A6FF"), ParseColor("#CC7DC7FF"), ParseColor("#00000000"), ParseColor("#FFE5EEFC"), ParseColor("#577AADFF"), ParseColor("#F0142034"),
            ParseColor("#FFE5EEFC"), ParseColor("#9D7DC7FF"), ParseColor("#F61A2940"), ParseColor("#FFF4FAFF"), ParseColor("#C67DC7FF"), ParseColor("#F0121D30"),
            ParseColor("#FF7DC7FF"), ParseColor("#A66E9AD7"), ParseColor("#F71A2A44"), ParseColor("#FFF3FAFF"), ParseColor("#D07DC7FF"));
    }

    private static Color ParseColor(string hex)
    {
        string value = hex.TrimStart('#');
        if (value.Length == 6)
        {
            value = "FF" + value;
        }

        if (value.Length != 8)
        {
            throw new ArgumentException($"Unexpected color value '{hex}'.", nameof(hex));
        }

        return Color.FromArgb(
            byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private sealed record ThemePalette(
        Color BoardColorBg,
        Color BoardColorBgElevated,
        Color BoardColorSurface,
        Color BoardColorSurfaceStrong,
        Color BoardColorSurfaceMuted,
        Color BoardColorBorder,
        Color BoardColorBorderStrong,
        Color BoardColorText,
        Color BoardColorTextMuted,
        Color BoardColorTextSoft,
        Color BoardColorAccent,
        Color BoardColorAccentStrong,
        Color BoardColorAccentSoft,
        Color BoardColorOverlay,
        Color BoardStatusRunningColor,
        Color BoardStatusCompletedColor,
        Color BoardStatusFailedColor,
        Color BoardStatusBlockedColor,
        Color BoardStatusFreshColor,
        Color BoardStatusUnknownColor,
        Color BoardWindowBackgroundGradientStart,
        Color BoardWindowBackgroundGradientMid,
        Color BoardWindowBackgroundGradientEnd,
        double BoardWindowBackgroundMidOffset,
        Color BoardTopBarBackgroundGradientStart,
        Color BoardTopBarBackgroundGradientEnd,
        Color BoardCanvasPanelColor,
        Color BoardCanvasPanelBorderColor,
        Color BoardCanvasMiniMapColor,
        Color BoardCanvasMiniMapBorderColor,
        Color BoardCanvasMiniMapViewportColor,
        Color BoardCanvasMiniMapViewportBorderColor,
        Color BoardToolbarButtonBackgroundColor,
        Color BoardToolbarButtonForegroundColor,
        Color BoardToolbarButtonBorderColor,
        Color BoardFloatingButtonBackgroundColor,
        Color BoardFloatingButtonForegroundColor,
        Color BoardFloatingButtonBorderColor,
        Color BoardFloatingButtonActiveBackgroundColor,
        Color BoardFloatingButtonActiveForegroundColor,
        Color BoardFloatingButtonActiveBorderColor,
        Color BoardEdgeToggleButtonBackgroundColor,
        Color BoardEdgeToggleButtonForegroundColor,
        Color BoardEdgeToggleButtonBorderColor,
        Color BoardEdgeToggleButtonActiveBackgroundColor,
        Color BoardEdgeToggleButtonActiveForegroundColor,
        Color BoardEdgeToggleButtonActiveBorderColor);
}