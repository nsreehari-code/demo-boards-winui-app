using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>FileUpload.jsx</c> — a file-picker control. Because WinUI has no drag-drop browser surface,
/// this opens the native picker (<see cref="NativeFilePicker"/>) on click and normalises the selection
/// into an array handed to <c>OnFiles</c>. <c>Variant</c> selects a plain button or a bordered dropzone.
/// </summary>
public sealed record FileUploadProps(
    bool Multiple = false,
    bool Disabled = false,
    string Variant = "dropzone",
    string Label = "Choose files",
    Action<IReadOnlyList<NativeAttachmentFile>>? OnFiles = null,
    Element? Children = null);

public sealed class FileUpload : Component<FileUploadProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (files, setFiles) = UseState<IReadOnlyList<NativeAttachmentFile>>(Array.Empty<NativeAttachmentFile>());

        async void Open()
        {
            if (Props.Disabled)
            {
                return;
            }

            IReadOnlyList<NativeAttachmentFile> picked = await NativeFilePicker.PickMultipleAttachmentsAsync(Props.Multiple);
            if (picked.Count == 0)
            {
                return;
            }

            setFiles(picked);
            Props.OnFiles?.Invoke(picked);
        }

        Element trigger = Button(Props.Label, () => Open())
            .AutomationName(Props.Label)
            .Set(button => button.IsEnabled = !Props.Disabled);

        Element surface = Props.Variant == "dropzone"
            ? Border(VStack(6, Props.Children ?? Empty(), trigger))
                .Padding(14)
                .WithBorder(theme.CardBorder, 1)
                .CornerRadius(10)
            : trigger;

        Element chips = files.Count == 0
            ? Empty()
            : VStack(2, files
                .Select(file => (Element)TextBlock($"{file.Name} ({FormatSize(file.Size)})").FontSize(12).Opacity(0.8).Foreground(theme.TextPrimary))
                .ToArray());

        return VStack(6, surface, chips);
    }

    internal static string FormatSize(long size)
    {
        if (size <= 0)
        {
            return "Unknown size";
        }

        if (size < 1024)
        {
            return $"{size} B";
        }

        double kb = size / 1024.0;
        if (kb < 1024)
        {
            return $"{Math.Max(1, Math.Round(kb)):0} KB";
        }

        double mb = kb / 1024.0;
        if (mb < 1024)
        {
            return $"{mb.ToString(mb >= 100 ? "0" : "0.0")} MB";
        }

        double gb = mb / 1024.0;
        return $"{gb.ToString(gb >= 100 ? "0" : "0.0")} GB";
    }
}
