using DemoBoards_WinUI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

public sealed record BoardConfigSectionProps(Element Content);

public sealed class BoardConfigSection : Component<BoardConfigSectionProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        return Border(Props.Content)
            .Padding(14, 12, 14, 12)
            .Background(theme.SurfaceForTone("primary", 0x12))
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(12)
            .HorizontalAlignment(HorizontalAlignment.Stretch);
    }
}