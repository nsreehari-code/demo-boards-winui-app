using DemoBoards_WinUI.State;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed class GandalfChatPane : UserControl
{
    private readonly ChatPane InnerChatPane;

    public GandalfChatPane()
    {
        InnerChatPane = new ChatPane();
        Content = InnerChatPane;
    }

    public event EventHandler<ChatPopoutRequestedEventArgs> PopoutRequested
    {
        add => InnerChatPane.PopoutRequested += value;
        remove => InnerChatPane.PopoutRequested -= value;
    }

    public void Bind(BoardStore boardStore, EmbeddedBoardClient boardClient, string cardId, bool compact = false)
    {
        InnerChatPane.Configure(compact, enablePopout: compact, title: "Chat");
        InnerChatPane.Bind(boardStore, boardClient, cardId);
    }
}