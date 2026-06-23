using System;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorGlobalModalProps(string Title, Action CloseAction, Element Body);

public sealed class ReactorGlobalModalComponent : Component<ReactorGlobalModalProps>
{
    public override Element Render()
    {
        return Border(
                VStack(10,
                    HStack(12,
                        TextBlock(Props.Title).Bold().Flex(grow: 1),
                        Button("Close", Props.CloseAction).AutomationName($"Close {Props.Title}").SubtleButton()),
                    Props.Body))
            .Padding(16)
            .Background(BoardTheme.ResolveBrush("CardBackgroundFillColorSecondaryBrush", Colors.WhiteSmoke))
            .WithBorder(BoardTheme.ResolveBrush("BoardBorderStrongBrush", Colors.LightGray), 1)
            .CornerRadius(16);
    }
}