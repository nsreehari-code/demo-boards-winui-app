using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>Notes.jsx</c> — a self-contained notes editor. Owns its draft journal and the
/// dirty-gated Discard / Save buttons; the caller supplies <c>BaseContent</c> and <c>OnSave</c>.
/// </summary>
public sealed record NotesProps(string BaseContent = "", string Placeholder = "Write markdown...", Action<string>? OnSave = null);

public sealed class Notes : Component<NotesProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (journal, setJournal) = UseState<string?>(null);

        bool dirty = journal != null;
        string effective = journal ?? Props.BaseContent;

        Element editor = TextBox(effective, value => setJournal(value == Props.BaseContent ? null : value))
            .AutomationName("Notes editor")
            .PlaceholderText(Props.Placeholder)
            .AcceptsReturn(true)
            .TextWrapping(TextWrapping.Wrap)
            .Foreground(theme.TextPrimary)
            .Set(textBox => textBox.MinHeight = 160)
            .Flex(grow: 1);

        var actions = new List<Element>();
        if (dirty)
        {
            actions.Add(Button("Discard", () => setJournal(null)).SubtleButton().AutomationName("Discard notes"));
            actions.Add(Button("Save", () => Props.OnSave?.Invoke(effective)).AccentButton().AutomationName("Save notes"));
        }

        return VStack(8, editor, HStack(8, actions.ToArray()));
    }
}
