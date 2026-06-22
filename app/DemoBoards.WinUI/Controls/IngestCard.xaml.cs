using DemoBoards.RuntimeHost;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class IngestCard : UserControl
{
    private readonly Border ShellBorder;
    private readonly TextBlock TitleText;
    private readonly TextBlock SubtitleText;
    private readonly GandalfChatPane ChatPaneView;

    public IngestCard()
    {
        TitleText = new TextBlock { FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.WrapWholeWords };
        SubtitleText = new TextBlock { FontSize = 12, Opacity = 0.68, TextWrapping = TextWrapping.WrapWholeWords };
        ChatPaneView = new GandalfChatPane();
        ShellBorder = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowSpacing = 10,
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                },
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 3,
                        Children = { TitleText, SubtitleText }
                    }
                }
            }
        };
        Grid.SetRow(ChatPaneView, 1);
        ((Grid)ShellBorder.Child).Children.Add(ChatPaneView);
        Content = ShellBorder;
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
