using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>The payload handed to <c>OnSubmit</c> — mirrors <c>{ text, files }</c>.</summary>
public sealed record MessageSubmitPayload(string Text, IReadOnlyList<NativeAttachmentFile> Files);

/// <summary>
/// Mirrors <c>MessageWithAttachmentsInput.jsx</c> — a text field + attachment picker + staged-file chips
/// behind a single submit action. It owns its own text/staged-file state; the staged files and trimmed
/// text are handed to <c>OnSubmit</c> together. <c>RequireText</c>/<c>RequireAttachment</c> gate submit.
/// </summary>
public sealed record MessageWithAttachmentsInputProps(
    bool Multiple = false,
    bool Disabled = false,
    bool Multiline = false,
    string Placeholder = "",
    string SubmitLabel = "Send",
    bool RequireText = false,
    bool RequireAttachment = false,
    Action<MessageSubmitPayload>? OnSubmit = null);

public sealed class MessageWithAttachmentsInput : Component<MessageWithAttachmentsInputProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (text, setText) = UseState(string.Empty);
        var (files, setFiles) = UseState<IReadOnlyList<NativeAttachmentFile>>(Array.Empty<NativeAttachmentFile>());

        async void Attach()
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

            IEnumerable<NativeAttachmentFile> accepted = Props.Multiple ? picked : picked.Take(1);
            var merged = files.ToList();
            foreach (NativeAttachmentFile file in accepted)
            {
                if (!merged.Any(entry => entry.Name == file.Name && entry.Size == file.Size))
                {
                    merged.Add(file);
                }
            }

            setFiles(merged);
        }

        string trimmed = text.Trim();
        bool hasText = trimmed.Length > 0;
        bool hasFiles = files.Count > 0;
        bool meetsContent = (Props.RequireText, Props.RequireAttachment) switch
        {
            (true, true) => hasText && hasFiles,
            (true, false) => hasText,
            (false, true) => hasFiles,
            _ => hasText || hasFiles,
        };
        bool canSubmit = meetsContent && !Props.Disabled;

        void Submit()
        {
            if (!canSubmit)
            {
                return;
            }

            Props.OnSubmit?.Invoke(new MessageSubmitPayload(trimmed, files.ToList()));
            setText(string.Empty);
            setFiles(Array.Empty<NativeAttachmentFile>());
        }

        Element chips = hasFiles
            ? VStack(2, files
                .Select((file, index) => (Element)HStack(6,
                    TextBlock($"{file.Name} ({FileUpload.FormatSize(file.Size)})").FontSize(12).Foreground(theme.TextPrimary).Flex(grow: 1),
                    Button("\u2715", () => setFiles(files.Where((_, i) => i != index).ToList()))
                        .SubtleButton().AutomationName($"Remove {file.Name}")))
                .ToArray())
            : Empty();

        var input = TextBox(text, setText).PlaceholderText(Props.Placeholder).Flex(grow: 1);
        Element field = Props.Multiline ? input.AcceptsReturn(true).TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap) : input;

        Element row = HStack(6,
            Button("\uE723", () => Attach()).SubtleButton().AutomationName("Attach files").Set(button => button.IsEnabled = !Props.Disabled),
            field,
            Button(Props.SubmitLabel, Submit).AccentButton().AutomationName(Props.SubmitLabel).Set(button => button.IsEnabled = canSubmit));

        return VStack(6, chips, row);
    }
}
