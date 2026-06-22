using DemoBoards.RuntimeHost;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed partial class IngestCard : UserControl
{
    public IngestCard()
    {
        InitializeComponent();
        ChatPaneView.PopoutRequested += OnChatPopoutRequested;
    }

    public void Render(BoardCard card)
    {
        TitleText.Text = card.Title;
        SubtitleText.Text = $"{card.Id}  •  Live ingest chat";
        ShellBorder.BorderBrush = CardShell.CreateToneBrush(card.Status, 0x44);
        ShellBorder.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        ChatPaneView.Bind(((App)Application.Current).BoardStore, ((App)Application.Current).BoardClient, card.Id, compact: true);
    }

    private void OnChatPopoutRequested(object? sender, ChatPopoutRequestedEventArgs e)
    {
        MainPage.TryGetCurrent()?.ShowChatPopout(e.CardId, e.Title);
    }
}
