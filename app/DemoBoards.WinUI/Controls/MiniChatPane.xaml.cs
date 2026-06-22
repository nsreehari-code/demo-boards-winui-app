using DemoBoards_WinUI.State;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed class MiniChatPane : UserControl
{
    private readonly ChatPane InnerChatPane;

    public MiniChatPane()
    {
        InnerChatPane = new ChatPane();
        Content = InnerChatPane;
        InnerChatPane.Configure(compact: true, enablePopout: true, title: "Chat");
    }

    public event EventHandler<ChatPopoutRequestedEventArgs> PopoutRequested
    {
        add => InnerChatPane.PopoutRequested += value;
        remove => InnerChatPane.PopoutRequested -= value;
    }

    public void Bind(BoardStore boardStore, EmbeddedBoardClient boardClient, string cardId)
    {
        InnerChatPane.Bind(boardStore, boardClient, cardId);
    }
}