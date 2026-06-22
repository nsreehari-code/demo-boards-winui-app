using System;
using DemoBoards.RuntimeHost;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed partial class StrategistCard : UserControl
{
    private string currentCardId = string.Empty;

    public StrategistCard()
    {
        InitializeComponent();
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
