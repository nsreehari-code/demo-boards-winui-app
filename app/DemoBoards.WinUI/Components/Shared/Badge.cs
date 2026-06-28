using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>Mirrors <c>Badge.jsx</c> — semantic status pill. <c>Tone</c> accepts the same keys/aliases.</summary>
public sealed record BadgeProps(string Value = "", string Tone = "secondary");

public sealed class Badge : Component<BadgeProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        return Border(TextBlock(Props.Value).FontSize(12).Foreground(theme.TextOnAccent))
            .Background(theme.BrushForTone(Props.Tone))
            .CornerRadius(10)
            .Set(border => border.Padding = new Thickness(8, 2, 8, 2));
    }
}
