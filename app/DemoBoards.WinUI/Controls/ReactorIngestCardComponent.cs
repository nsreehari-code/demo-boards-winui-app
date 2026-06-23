using DemoBoards.RuntimeHost;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorIngestCardProps(BoardCard Card);

public sealed class ReactorIngestCardComponent : Component<ReactorIngestCardProps>
{
    public override Element Render()
    {
        BoardCard card = Props.Card;

        return Border(
                Component<ReactorChatPaneComponent, ReactorChatPaneProps>(
                    new ReactorChatPaneProps(
                        App.Current.BoardStore,
                        App.Current.BoardClient,
                        card.Id,
                        Compact: true,
                        EnablePopout: true,
                        Title: card.Title)))
            .Padding(12)
            .Background(ReactorMainShellComponent.ResolveBrush("CardBackgroundFillColorDefaultBrush"))
            .WithBorder(CardToneBrushes.CreateToneBrush(card.Status, 0x44), 1)
            .CornerRadius(14);
    }
}