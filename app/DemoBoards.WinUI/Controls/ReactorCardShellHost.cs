using DemoBoards.RuntimeHost;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed class ReactorCardShellHost : UserControl
{
    private readonly ReactorHostControl host;
    private BoardCard? currentCard;

    public ReactorCardShellHost()
    {
        host = new ReactorHostControl();
        Content = host;
    }

    public void Render(BoardCard card)
    {
        currentCard = card;
        host.Mount(new ReactorCardShellComponent(card, RequestRender));
    }

    private void RequestRender()
    {
        if (currentCard is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => host.Mount(new ReactorCardShellComponent(currentCard, RequestRender)));
    }
}