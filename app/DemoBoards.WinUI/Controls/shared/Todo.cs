using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>One todo entry — mirrors the frontend item shape <c>{ text, done }</c>.</summary>
public sealed record TodoItem(string Text, bool Done = false);

/// <summary>
/// Mirrors <c>Todo.jsx</c> — a self-contained todo list. Owns its draft items, the add composer and
/// per-item toggle/remove; <c>OnSave</c> fires with the committed items whenever the list mutates.
/// </summary>
public sealed record TodoProps(IReadOnlyList<TodoItem> BaseItems, Action<IReadOnlyList<TodoItem>>? OnSave = null);

public sealed class Todo : Component<TodoProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (pending, setPending) = UseState<IReadOnlyList<TodoItem>>(Props.BaseItems ?? Array.Empty<TodoItem>());
        var (composerText, setComposerText) = UseState(string.Empty);

        void Save(IReadOnlyList<TodoItem> next)
        {
            setPending(next);
            Props.OnSave?.Invoke(next);
        }

        Element[] rows = pending.Select((item, index) =>
        {
            int rowIndex = index;
            Element check = CheckBox((bool?)item.Done, done =>
            {
                List<TodoItem> next = pending.ToList();
                next[rowIndex] = next[rowIndex] with { Done = done };
                Save(next);
            }, string.Empty);

            Element label = TextBlock(item.Text)
                .FontSize(12)
                .Foreground(theme.TextPrimary)
                .Set(text =>
                {
                    text.TextDecorations = item.Done ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
                    text.Opacity = item.Done ? 0.6 : 1.0;
                })
                .Flex(grow: 1);

            Element remove = Button("\u00d7", () => Save(pending.Where((_, i) => i != rowIndex).ToList()))
                .SubtleButton()
                .AutomationName($"Remove {item.Text}");

            return (Element)HStack(8, check, label, remove);
        }).ToArray();

        void Add()
        {
            string text = composerText.Trim();
            if (text.Length == 0)
            {
                return;
            }

            Save(pending.Append(new TodoItem(text)).ToList());
            setComposerText(string.Empty);
        }

        Element composer = HStack(8,
            TextBox(composerText, setComposerText).AutomationName("Add todo item").PlaceholderText("Add item...").Flex(grow: 1),
            Button("+", Add).SubtleButton().AutomationName("Add todo item"));

        var sections = new List<Element>();
        sections.AddRange(rows);
        sections.Add(composer);
        return VStack(8, sections.ToArray());
    }
}
