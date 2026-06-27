using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.System;
using Windows.UI.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>MessageWithAttachmentsInput.jsx</c> — a text field + attachment picker + (optionally)
/// staged-file chips behind a single submit action. It owns its own text/staged-file state. Two
/// attachment models: <c>Staged</c> (default) keeps files as chips and hands them to <c>OnSubmit({text,files})</c>;
/// immediate (<c>Staged=false</c>) forwards each selection to <c>OnAttach</c> right away while <c>OnSubmit</c>
/// only carries the text. <c>RequireText</c>/<c>RequireAttachment</c> gate submit; <c>SubmitOnEnter</c>
/// submits on Enter (Shift+Enter inserts a newline when <c>Multiline</c>).
/// </summary>
/// <remarks>DOM-only render/style slots (className, dropzone/attach content, renderChip) are dropped;
/// the submit-content slot is supported via <c>SubmitIcon</c> (renders an SVG glyph in place of the label).</remarks>
public sealed record MessageWithAttachmentsInputProps(
    Action<IReadOnlyDictionary<string, object?>>? OnSubmit = null,
    Action<IReadOnlyList<IReadOnlyDictionary<string, object?>>>? OnAttach = null,
    bool Staged = true,
    bool Multiple = false,
    bool Disabled = false,
    IReadOnlyList<string>? Accept = null,
    bool RequireText = false,
    bool RequireAttachment = false,
    bool Multiline = false,
    bool? AutoResize = null,
    string Placeholder = "",
    bool SubmitOnEnter = true,
    string SubmitLabel = "Send",
    string? SubmitIcon = null);

public sealed class MessageWithAttachmentsInput : Component<MessageWithAttachmentsInputProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (text, setText) = UseState(string.Empty);
        var (files, setFiles) = UseState<IReadOnlyList<NativeAttachmentFile>>(Array.Empty<NativeAttachmentFile>());

        void AddFiles(IReadOnlyList<NativeAttachmentFile> incoming)
        {
            if (incoming.Count == 0)
            {
                return;
            }

            IReadOnlyList<NativeAttachmentFile> accepted = (Props.Multiple ? incoming : incoming.Take(1)).ToList();
            if (Props.Staged)
            {
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
            else
            {
                Props.OnAttach?.Invoke(accepted.Select(file => file.ToData()).ToList());
            }
        }

        async void Attach()
        {
            if (Props.Disabled)
            {
                return;
            }

            IReadOnlyList<NativeAttachmentFile> picked = await NativeFilePicker.PickMultipleAttachmentsAsync(Props.Multiple, Props.Accept);
            AddFiles(picked);
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

            Props.OnSubmit?.Invoke(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["text"] = trimmed,
                ["files"] = files.Select(file => file.ToData()).ToList(),
            });
            setText(string.Empty);
            setFiles(Array.Empty<NativeAttachmentFile>());
        }

        Element chips = Props.Staged && hasFiles
            ? VStack(2, files
                .Select((file, index) => (Element)HStack(6,
                    TextBlock($"{file.Name} ({FileUpload.FormatSize(file.Size)})").FontSize(12).Foreground(theme.TextPrimary).Flex(grow: 1),
                    Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.X, 13)), () => setFiles(files.Where((_, i) => i != index).ToList()))
                        .SubtleButton().AutomationName($"Remove {file.Name}")))
                .ToArray())
            : Empty();

        bool autoResize = Props.AutoResize ?? Props.Multiline;
        var input = TextBox(text, setText)
            .AutomationName(string.IsNullOrEmpty(Props.Placeholder) ? "Message" : Props.Placeholder)
            .PlaceholderText(Props.Placeholder)
            .Flex(grow: 1);
        var field = Props.Multiline
            ? input.AcceptsReturn(true).TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap)
            : input;

        Element configured = field.Set(box =>
        {
            box.IsEnabled = !Props.Disabled;
            if (autoResize)
            {
                box.MaxHeight = 160;
            }

            box.KeyDown += (_, e) =>
            {
                if (!Props.SubmitOnEnter || e.Key != VirtualKey.Enter)
                {
                    return;
                }

                bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
                if (!Props.Multiline || !shift)
                {
                    e.Handled = true;
                    Submit();
                }
            };
        });

        Element submitContent = string.IsNullOrEmpty(Props.SubmitIcon)
            ? (Element)TextBlock(Props.SubmitLabel)
            : Component<SvgIcon, SvgIconProps>(new SvgIconProps(Props.SubmitIcon, 16));

        Element row = HStack(6,
            Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ChatAttach, 16)), () => Attach()).SubtleButton().AutomationName("Attach files").Set(button => button.IsEnabled = !Props.Disabled),
            configured,
            Button(submitContent, Submit).AccentButton().AutomationName(Props.SubmitLabel).Set(button => button.IsEnabled = canSubmit));

        return VStack(6, chips, row);
    }
}
