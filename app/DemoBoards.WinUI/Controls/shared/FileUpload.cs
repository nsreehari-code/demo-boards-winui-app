using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>FileUpload.jsx</c> — a file-picker / drop surface. Opens the native picker
/// (<see cref="NativeFilePicker"/>) on tap, honouring the <c>Accept</c> filter, and (when
/// <c>EnableDrop</c>) accepts files dropped onto the surface — normalising the selection into an array
/// handed to <c>OnFiles</c>. <c>Variant</c> <c>"dropzone"</c> renders the clickable surface (its visible
/// content is <c>Children</c>); <c>"input"</c> renders nothing and is driven imperatively via the
/// <c>OnReady(open)</c> callback (the parity equivalent of the frontend ref's <c>open()</c> method).
/// </summary>
/// <remarks>DOM-only props (<c>as</c>, <c>className</c>/<c>activeClassName</c>/<c>disabledClassName</c>) are dropped.</remarks>
public sealed record FileUploadProps(
    Action<IReadOnlyList<IReadOnlyDictionary<string, object?>>>? OnFiles = null,
    IReadOnlyList<string>? Accept = null,
    bool Multiple = false,
    bool Disabled = false,
    string Variant = "dropzone",
    bool EnableDrop = true,
    Action<Action>? OnReady = null,
    Element? Children = null);

public sealed class FileUpload : Component<FileUploadProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        async void Open()
        {
            if (Props.Disabled)
            {
                return;
            }

            IReadOnlyList<NativeAttachmentFile> picked = await NativeFilePicker.PickMultipleAttachmentsAsync(Props.Multiple, Props.Accept);
            if (picked.Count > 0)
            {
                Props.OnFiles?.Invoke(picked.Select(file => file.ToData()).ToList());
            }
        }

        UseEffect(() =>
        {
            Props.OnReady?.Invoke(Open);
            return null;
        }, "file-upload-ready");

        if (Props.Variant == "input")
        {
            return Empty();
        }

        Element content = Props.Children ?? TextBlock("Choose files").Foreground(theme.TextPrimary);

        return Border(content)
            .Padding(14)
            .Background(theme.Layer)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(10)
            .AutomationName("File upload")
            .Set(border =>
            {
                border.Opacity = Props.Disabled ? 0.5 : 1.0;
                border.Tapped += (_, _) => Open();
                if (!Props.EnableDrop)
                {
                    return;
                }

                border.AllowDrop = true;
                border.DragOver += (_, e) => e.AcceptedOperation = DataPackageOperation.Copy;
                border.Drop += async (_, e) =>
                {
                    if (Props.Disabled || !e.DataView.Contains(StandardDataFormats.StorageItems))
                    {
                        return;
                    }

                    IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
                    IReadOnlyList<NativeAttachmentFile> files = await NativeFilePicker.ReadAttachmentsAsync(items);
                    if (files.Count > 0)
                    {
                        Props.OnFiles?.Invoke(files.Select(file => file.ToData()).ToList());
                    }
                };
            });
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
