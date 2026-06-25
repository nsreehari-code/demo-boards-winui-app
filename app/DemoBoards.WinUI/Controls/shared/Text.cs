using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>A staged/stored file descriptor for <see cref="Text"/>'s <c>file-links</c> format.</summary>
public sealed record TextFile(string? Name = null, string? StoredName = null, double? Size = null);

/// <summary>
/// Mirrors <c>Text.jsx</c> — a text / file-link renderer. <c>Value</c> is a string for the default
/// format, or an <see cref="IReadOnlyList{TextFile}"/> when <c>Format == "file-links"</c>.
/// </summary>
public sealed record TextProps(
    object? Value = null,
    string Format = "default",
    string Style = "default",
    bool HideIfEmpty = false,
    Func<int, TextFile, string?>? ResolveFileUrl = null);

public sealed class Text : Component<TextProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        if (Props.HideIfEmpty && IsEmpty(Props.Value))
        {
            return Empty();
        }

        if (Props.Format == "file-links")
        {
            return RenderFileLinks(theme);
        }

        string text = Props.Value != null ? BoardShared.Stringify(Props.Value) : string.Empty;
        var block = TextBlock(text)
            .Foreground(theme.TextPrimary)
            .Set(element => element.TextWrapping = TextWrapping.WrapWholeWords);

        return Props.Style switch
        {
            "heading" => block.FontSize(18).Bold(),
            "muted" => block.FontSize(12).Opacity(0.65),
            "muted-italic" => block.FontSize(12).Opacity(0.65).Set(element => element.FontStyle = Windows.UI.Text.FontStyle.Italic),
            _ => block.FontSize(12),
        };
    }

    private Element RenderFileLinks(AppTheme theme)
    {
        if (Props.Value is not IReadOnlyList<TextFile> files || files.Count == 0)
        {
            return TextBlock("No files uploaded").FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary);
        }

        var rows = new List<Element>();
        for (int index = 0; index < files.Count; index++)
        {
            TextFile file = files[index];
            if (file is null || string.IsNullOrEmpty(file.StoredName))
            {
                continue;
            }

            string name = file.Name ?? file.StoredName!;
            string size = file.Size is > 0 ? $" ({Math.Round(file.Size.Value / 1024)}KB)" : string.Empty;
            string label = $"{name}{size}";
            string? href = Props.ResolveFileUrl?.Invoke(index, file);

            Element link = Button(label, () =>
                {
                    if (!string.IsNullOrEmpty(href) && Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
                    {
                        _ = Windows.System.Launcher.LaunchUriAsync(uri);
                    }
                })
                .SubtleButton()
                .AutomationName(label)
                .Set(control => control.IsEnabled = !string.IsNullOrEmpty(href));

            rows.Add(link);
        }

        return rows.Count == 0
            ? TextBlock("No files uploaded").FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary)
            : VStack(8, rows.ToArray());
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        IReadOnlyList<TextFile> files => files.Count == 0,
        _ => false,
    };
}
