using System;
using DemoBoards.RuntimeHost;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class StrategistCard : UserControl
{
    private string currentCardId = string.Empty;
    private readonly Border ShellBorder;
    private readonly TextBlock TitleText;
    private readonly TextBlock MetaText;
    private readonly Border PathStateBadge;
    private readonly TextBlock PathStateText;
    private readonly Border StatusBadge;
    private readonly TextBlock StatusText;
    private readonly Button RefreshButton;
    private readonly TextBlock RefreshStatusText;
    private readonly Grid CoreHost;

    public StrategistCard()
    {
        TitleText = new TextBlock
        {
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        MetaText = new TextBlock
        {
            Opacity = 0.68,
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        PathStateText = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        PathStateBadge = new Border
        {
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(10),
            Child = PathStateText
        };
        StatusText = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        StatusBadge = new Border
        {
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(10),
            Child = StatusText
        };
        RefreshButton = new Button { Content = "Refresh" };
        RefreshStatusText = new TextBlock
        {
            Opacity = 0.72,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        CoreHost = new Grid();

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                TitleText,
                MetaText,
            }
        });

        var badges = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                PathStateBadge,
                StatusBadge,
                RefreshButton,
            }
        };
        Grid.SetColumn(badges, 1);
        header.Children.Add(badges);

        var layout = new Grid { RowSpacing = 10 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(header);
        Grid.SetRow(RefreshStatusText, 1);
        layout.Children.Add(RefreshStatusText);
        Grid.SetRow(CoreHost, 2);
        layout.Children.Add(CoreHost);

        ShellBorder = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = layout
        };
        Content = ShellBorder;
        RefreshButton.Click += OnRefreshClick;
    }

    public void Render(BoardCard card)
    {
        currentCardId = card.Id;
        TitleText.Text = card.Title;
        MetaText.Text = $"{card.Id}  •  {string.Join(", ", card.ViewKinds)}";
        StatusText.Text = card.Status;
        StatusBadge.Background = CardShell.CreateToneBrush(card.Status, 0x22);
        ShellBorder.BorderBrush = CardShell.CreateToneBrush(card.Status, 0x44);
        ShellBorder.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];

        string pathState = card.MetaValues.TryGetValue("path_state", out string? rawPathState)
            ? (rawPathState ?? string.Empty).Trim().ToLowerInvariant()
            : string.Empty;
        if (pathState is "suspended" or "dead_ended" or "wiped")
        {
            PathStateBadge.Visibility = Visibility.Visible;
            PathStateText.Text = pathState switch
            {
                "suspended" => "Suspended",
                "dead_ended" => "Ruled out",
                "wiped" => "Wiped",
                _ => pathState
            };
            PathStateBadge.Background = pathState switch
            {
                "suspended" => CardShell.CreateToneBrush("blocked", 0x24),
                "dead_ended" => CardShell.CreateToneBrush("failed", 0x24),
                _ => CardShell.CreateToneBrush("fresh", 0x18)
            };
            if (card.MetaValues.TryGetValue("path_state_rationale", out string? rationale) && !string.IsNullOrWhiteSpace(rationale))
            {
                ToolTipService.SetToolTip(PathStateBadge, rationale);
            }
        }
        else
        {
            PathStateBadge.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(PathStateBadge, null);
        }

        bool canRefresh = ((App)Application.Current).BoardStore.GetCardState(card.Id)?.CanRefresh == true;
        RefreshButton.Visibility = canRefresh ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = canRefresh && !string.Equals(card.Status, "running", StringComparison.OrdinalIgnoreCase);
        RefreshStatusText.Text = string.Equals(card.Status, "running", StringComparison.OrdinalIgnoreCase)
            ? "Card runtime is currently refreshing."
            : string.Empty;

        CoreHost.Children.Clear();
        var core = new CardCore();
        core.Render(((App)Application.Current).BoardStore, card);
        CoreHost.Children.Add(core);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentCardId))
        {
            return;
        }

        RefreshButton.IsEnabled = false;
        RefreshStatusText.Text = "Refreshing strategist card...";
        try
        {
            await ((App)Application.Current).BoardClient.RefreshCardAsync(currentCardId);
            RefreshStatusText.Text = "Refresh dispatched.";
        }
        catch (Exception ex)
        {
            RefreshStatusText.Text = ex.Message;
        }
    }
}
