using DemoBoards.RuntimeHost;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class CardShell : UserControl
{
    private string currentCardId = string.Empty;
    private bool miniChatOpen;
    private readonly Border ShellBorder;
    private readonly Grid FrontHost;
    private readonly CardBackface BackfaceView;
    private readonly Button InspectButton;
    private readonly Button ChatButton;
    private readonly Button BackButton;

    public CardShell()
    {
        FrontHost = new Grid();
        BackfaceView = new CardBackface
        {
            Visibility = Visibility.Collapsed
        };
        InspectButton = new Button { Content = "Inspect" };
        ChatButton = new Button { Content = "Chat" };
        BackButton = new Button
        {
            Content = "Back",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var overlayButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                InspectButton,
                ChatButton,
            }
        };

        var layout = new Grid();
        layout.Children.Add(FrontHost);
        layout.Children.Add(BackfaceView);
        layout.Children.Add(overlayButtons);
        layout.Children.Add(BackButton);

        ShellBorder = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = layout
        };
        Content = ShellBorder;
        BackButton.Click += (_, _) => ShowFront();
        InspectButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(currentCardId)) return;
            ((App)Application.Current).BoardStore.SetInspectedCardId(currentCardId);
        };
        ChatButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(currentCardId)) return;
            App app = (App)Application.Current;
            app.BoardStore.SetMiniChatOpen(currentCardId, !app.BoardStore.IsMiniChatOpen(currentCardId));
        };
    }

    public void Render(BoardCard card)
    {
        currentCardId = card.Id;
        miniChatOpen = ((App)Application.Current).BoardStore.IsMiniChatOpen(card.Id);
        ShellBorder.BorderBrush = CreateToneBrush(card.Status, 0x66);
        ShellBorder.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        InspectButton.Content = "Inspect runtime";
        ChatButton.Content = miniChatOpen ? "Hide chat" : card.ChatProcessing ? "Chat..." : "Chat";
        BackButton.Content = "Back to card";

        FrontHost.Children.Clear();
        BackfaceView.Render(card);
        BuildFront(card);
    }

    internal static Brush CreateToneBrush(string status, byte alpha)
    {
        return BoardTheme.CreateStatusBrush(status, alpha);
    }

    private void BuildFront(BoardCard card)
    {
        var layout = new Grid { RowSpacing = 8 };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var stack = new StackPanel { Spacing = 8 };
        Grid.SetRow(stack, 0);

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBlock = new StackPanel { Spacing = 3 };
        titleBlock.Children.Add(new TextBlock
        {
            Text = card.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = card.ViewKinds.Count > 0 ? $"{card.Id}  •  {string.Join(", ", card.ViewKinds)}" : card.Id,
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        header.Children.Add(titleBlock);
        var statusChip = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Top,
            Background = CreateToneBrush(card.Status, 0x33),
            Visibility = string.Equals(card.Status, "completed", StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible,
            Child = new TextBlock
            {
                Text = card.Status,
                FontSize = 12,
                Foreground = CreateToneBrush(card.Status, 0xFF)
            }
        };
        Grid.SetColumn(statusChip, 1);
        header.Children.Add(statusChip);
        stack.Children.Add(header);

        if (card.MetaValues.TryGetValue("path_state", out string? pathState) && !string.IsNullOrWhiteSpace(pathState))
        {
            string rationale = card.MetaValues.TryGetValue("path_state_rationale", out string? pathStateRationale) ? pathStateRationale ?? string.Empty : string.Empty;
            var badge = new Border
            {
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(10),
                Background = CreateToneBrush(pathState, 0x16),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = pathState.Replace('_', ' '),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };
            if (!string.IsNullOrWhiteSpace(rationale))
            {
                ToolTipService.SetToolTip(badge, rationale);
            }
            stack.Children.Add(new Border
            {
                Child = badge
            });
        }

        if (card.ViewElements.Count > 0)
        {
            var core = new CardCore();
            core.Render(((App)Application.Current).BoardStore, card);

            if (miniChatOpen)
            {
                var miniChatPane = new MiniChatPane();
                miniChatPane.Bind(((App)Application.Current).BoardStore, ((App)Application.Current).BoardClient, card.Id);
                miniChatPane.PopoutRequested += OnMiniChatPopoutRequested;
                stack.Children.Add(miniChatPane);
            }

            stack.Children.Add(core);
        }
        else if (card.Fields.Count > 0)
        {
            if (miniChatOpen)
            {
                var miniChatPane = new MiniChatPane();
                miniChatPane.Bind(((App)Application.Current).BoardStore, ((App)Application.Current).BoardClient, card.Id);
                miniChatPane.PopoutRequested += OnMiniChatPopoutRequested;
                stack.Children.Add(miniChatPane);
            }

            stack.Children.Add(BuildFieldList(card.Fields));
        }

        if (card.Requires.Count > 0 || card.Provides.Count > 0)
        {
            stack.Children.Add(BuildTokenRow(card.Requires, card.Provides));
        }

        layout.Children.Add(stack);

        var button = new Button
        {
            Content = "Flip to runtime",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        button.Click += (_, _) =>
        {
            FrontHost.Visibility = Visibility.Collapsed;
            BackfaceView.Visibility = Visibility.Visible;
        };
        Grid.SetRow(button, 1);
        layout.Children.Add(button);

        FrontHost.Children.Add(layout);

        if (((App)Application.Current).BoardStore.IsFlipped(card.Id)) ShowBack();
        else ShowFront();
        button.Click += (_, _) => ShowBack();
    }

    private void OnMiniChatPopoutRequested(object? sender, ChatPopoutRequestedEventArgs e)
    {
        MainPage.TryGetCurrent()?.ShowChatPopout(e.CardId, e.Title);
    }

    private void ShowFront()
    {
        FrontHost.Visibility = Visibility.Visible;
        BackfaceView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(currentCardId))
        {
            ((App)Application.Current).BoardStore.SetFlipped(currentCardId, false);
        }
    }

    private void ShowBack()
    {
        FrontHost.Visibility = Visibility.Collapsed;
        BackfaceView.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(currentCardId))
        {
            ((App)Application.Current).BoardStore.SetFlipped(currentCardId, true);
        }
    }

    private static FrameworkElement BuildFieldList(System.Collections.Generic.IReadOnlyList<BoardCardField> fields)
    {
        var stack = new StackPanel { Spacing = 2 };
        foreach (BoardCardField field in fields)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            line.Children.Add(new TextBlock
            {
                Text = $"{field.Key}:",
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.8
            });
            line.Children.Add(new TextBlock
            {
                Text = field.Value,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            stack.Children.Add(line);
        }

        return stack;
    }

    private static FrameworkElement BuildTokenRow(System.Collections.Generic.IReadOnlyList<string> requires, System.Collections.Generic.IReadOnlyList<string> provides)
    {
        var stack = new StackPanel { Spacing = 4 };

        if (requires.Count > 0)
        {
            stack.Children.Add(BuildTokenChips("requires", requires, "fresh", 0x33));
        }

        if (provides.Count > 0)
        {
            stack.Children.Add(BuildTokenChips("provides", provides, "completed", 0x33));
        }

        return stack;
    }

    private static FrameworkElement BuildTokenChips(string label, System.Collections.Generic.IReadOnlyList<string> tokens, string tone, byte alpha)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        });

        foreach (string token in tokens)
        {
            row.Children.Add(new Border
            {
                Padding = new Thickness(8, 2, 8, 2),
                CornerRadius = new CornerRadius(8),
                Background = CreateToneBrush(tone, alpha),
                Child = new TextBlock { Text = token, FontSize = 12 }
            });
        }

        return row;
    }
}
