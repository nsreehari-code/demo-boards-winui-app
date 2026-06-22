using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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

    public static Uri GetThemeDictionaryUri(string themePackId)
    {
        string resourceName = NormalizeThemePackId(themePackId) == SignalRoomThemePackId ? "SignalRoom" : "MistOps";
        return new Uri($"ms-appx:///Themes/{resourceName}.xaml");
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

    public static string BuildWebViewCssVariables()
    {
        var builder = new StringBuilder();
        builder.Append(":root{");
        AppendCssColor(builder, "--color-bg", "BoardColorBg", Color.FromArgb(0xFF, 0xE4, 0xEB, 0xF2));
        AppendCssColor(builder, "--color-surface", "BoardColorSurface", Color.FromArgb(0xF0, 0xF9, 0xFB, 0xFD));
        AppendCssColor(builder, "--color-surface-strong", "BoardColorSurfaceStrong", Color.FromArgb(0xFA, 0xF5, 0xF8, 0xFB));
        AppendCssColor(builder, "--color-border", "BoardColorBorder", Color.FromArgb(0x2E, 0x47, 0x65, 0x86));
        AppendCssColor(builder, "--color-text", "BoardColorText", Color.FromArgb(0xFF, 0x16, 0x32, 0x4A));
        AppendCssColor(builder, "--color-text-muted", "BoardColorTextMuted", Color.FromArgb(0xFF, 0x5F, 0x76, 0x8E));
        AppendCssColor(builder, "--color-text-soft", "BoardColorTextSoft", Color.FromArgb(0xFF, 0x7A, 0x8E, 0xA4));
        AppendCssColor(builder, "--color-accent", "BoardColorAccent", Color.FromArgb(0xFF, 0x1F, 0x8F, 0xFF));
        AppendCssColor(builder, "--color-accent-strong", "BoardColorAccentStrong", Color.FromArgb(0xFF, 0x17, 0x6D, 0xD1));
        AppendCssColor(builder, "--status-running", "BoardStatusRunningColor", Color.FromArgb(0xFF, 0xD4, 0x8A, 0x2A));
        AppendCssColor(builder, "--status-completed", "BoardStatusCompletedColor", Color.FromArgb(0xFF, 0x18, 0xB5, 0x7D));
        AppendCssColor(builder, "--status-failed", "BoardStatusFailedColor", Color.FromArgb(0xFF, 0xD6, 0x45, 0x5D));
        AppendCssColor(builder, "--status-blocked", "BoardStatusBlockedColor", Color.FromArgb(0xFF, 0xD9, 0x92, 0x2E));
        AppendCssColor(builder, "--status-fresh", "BoardStatusFreshColor", Color.FromArgb(0xFF, 0x65, 0x86, 0xAA));
        AppendCssColor(builder, "--status-unknown", "BoardStatusUnknownColor", Color.FromArgb(0xFF, 0x81, 0x91, 0xA3));
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendCssColor(StringBuilder builder, string cssName, string resourceKey, Color fallback)
    {
        builder.Append(cssName);
        builder.Append(':');
        builder.Append(ToCssColor(ResolveColor(resourceKey, fallback)));
        builder.Append(';');
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static string ToCssColor(Color color)
    {
        if (color.A == 0xFF)
        {
            return string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }

        double alpha = Math.Round(color.A / 255d, 3);
        return FormattableString.Invariant($"rgba({color.R}, {color.G}, {color.B}, {alpha})");
    }
}