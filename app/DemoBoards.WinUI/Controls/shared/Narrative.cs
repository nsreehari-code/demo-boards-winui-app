using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>Mirrors <c>Narrative.jsx</c> — read-only narrative block with an empty state.</summary>
public sealed record NarrativeProps(string Text = "", string EmptyMessage = "No narrative yet.");

public sealed class Narrative : Component<NarrativeProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        if (string.IsNullOrEmpty(Props.Text))
        {
            return TextBlock(Props.EmptyMessage)
                .Opacity(0.6)
                .Foreground(theme.TextPrimary)
                .Set(text => text.FontStyle = Windows.UI.Text.FontStyle.Italic);
        }

        return TextBlock(Props.Text)
            .Foreground(theme.TextPrimary)
            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);
    }
}
