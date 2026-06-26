using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>ChatInput.jsx</c> — the chat composer. A thin, presentational wrapper over
/// <see cref="MessageWithAttachmentsInput"/> in immediate-attachment mode: each picked file is
/// forwarded to <c>OnAttach</c> right away and the trimmed message text is forwarded to <c>OnSubmit</c>.
/// It owns no chat data — the host wires <c>OnAttach</c>/<c>OnSubmit</c> to the real send/upload actions.
/// </summary>
public sealed record ChatInputProps(
    Action<string>? OnSubmit = null,
    Action<IReadOnlyList<IReadOnlyDictionary<string, object?>>>? OnAttach = null,
    bool Processing = false,
    string? Placeholder = null,
    string Variant = "default");

public sealed class ChatInput : Component<ChatInputProps>
{
    public override Element Render()
    {
        _ = UseContext(AppThemeContext.Current);

        return Component<MessageWithAttachmentsInput, MessageWithAttachmentsInputProps>(new MessageWithAttachmentsInputProps(
            Staged: false,
            Multiline: true,
            RequireText: true,
            Disabled: Props.Processing,
            Placeholder: Props.Placeholder ?? "Send a message\u2026",
            OnAttach: files =>
            {
                if (Props.Processing || files.Count == 0)
                {
                    return;
                }

                Props.OnAttach?.Invoke(files);
            },
            OnSubmit: payload =>
            {
                string text = (BoardData.Str(payload, "text") ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    return;
                }

                Props.OnSubmit?.Invoke(text);
            }));
    }
}
