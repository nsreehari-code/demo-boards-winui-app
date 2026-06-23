using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed class ReactorCardShellComponent : Component
{
    private readonly BoardCard card;
    private readonly Action requestRender;

    public ReactorCardShellComponent(BoardCard card, Action requestRender)
    {
        this.card = card;
        this.requestRender = requestRender;
    }

    public override Element Render()
    {
        App app = App.Current;
        bool miniChatOpen = app.BoardStore.IsMiniChatOpen(card.Id);
        bool flipped = app.BoardStore.IsFlipped(card.Id);
        bool canRefresh = app.BoardStore.GetCardState(card.Id)?.CanRefresh == true;

        var sections = new List<Element>
        {
            BuildHeader(),
        };

        Element? pathStateBadge = BuildPathStateBadge();
        if (pathStateBadge is not null)
        {
            sections.Add(pathStateBadge);
        }

        sections.Add(flipped ? BuildBackfaceHost() : BuildFrontHost(miniChatOpen));

        if (card.Requires.Count > 0 || card.Provides.Count > 0)
        {
            sections.Add(BuildTokenSection());
        }

        sections.Add(BuildActionRow(miniChatOpen, flipped, canRefresh));

        return Border(VStack(12, sections.ToArray()))
            .Padding(16)
            .Background((Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"])
            .WithBorder(CardToneBrushes.CreateToneBrush(card.Status, 0x66), 1)
            .CornerRadius(14);
    }

    private Element BuildHeader()
    {
        string subtitle = card.ViewKinds.Count > 0
            ? $"{card.Id}  •  {string.Join(", ", card.ViewKinds)}"
            : card.Id;

        var headerItems = new List<Element>
        {
            VStack(3,
                TextBlock(card.Title).Bold(),
                TextBlock(subtitle).Opacity(0.6))
                .Flex(grow: 1),
        };

        if (!string.Equals(card.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            headerItems.Add(
                Border(TextBlock(card.Status).Foreground(CardToneBrushes.CreateToneBrush(card.Status, 0xFF)))
                    .Background(CardToneBrushes.CreateToneBrush(card.Status, 0x33))
                    .CornerRadius(8)
                    .Padding(8));
        }

        return HStack(8, headerItems.ToArray());
    }

    private Element? BuildPathStateBadge()
    {
        if (!card.MetaValues.TryGetValue("path_state", out string? pathState) || string.IsNullOrWhiteSpace(pathState))
        {
            return null;
        }

        return Border(TextBlock(pathState.Replace('_', ' ')).Bold())
            .Background(CardToneBrushes.CreateToneBrush(pathState, 0x16))
            .CornerRadius(10)
            .Padding(8);
    }

    private Element BuildFrontHost(bool miniChatOpen)
    {
        var sections = new List<Element>();

        if (miniChatOpen)
        {
            sections.Add(Component<ReactorChatPaneComponent, ReactorChatPaneProps>(
                new ReactorChatPaneProps(
                    App.Current.BoardStore,
                    App.Current.BoardClient,
                    card.Id,
                    Compact: true,
                    EnablePopout: true,
                    Title: "Chat")));
        }

        if (card.ViewElements.Count > 0 || card.Fields.Count > 0)
        {
            sections.Add(Component<ReactorCardFrontContentComponent, ReactorCardFrontContentProps>(
                new ReactorCardFrontContentProps(card)));
        }

        return VStack(8, sections.ToArray());
    }

    private Element BuildBackfaceHost()
    {
        return Component<ReactorCardBackfaceComponent, ReactorCardBackfaceProps>(new ReactorCardBackfaceProps(card));
    }

    private Element BuildTokenSection()
    {
        var rows = new List<Element>();

        if (card.Requires.Count > 0)
        {
            rows.Add(BuildTokenRow("requires", card.Requires, "fresh", 0x33));
        }

        if (card.Provides.Count > 0)
        {
            rows.Add(BuildTokenRow("provides", card.Provides, "completed", 0x33));
        }

        return VStack(4, rows.ToArray());
    }

    private Element BuildTokenRow(string label, IReadOnlyList<string> tokens, string tone, byte alpha)
    {
        var items = new List<Element>
        {
            TextBlock(label).Opacity(0.6),
        };

        items.AddRange(tokens.Select(token =>
            (Element)Border(TextBlock(token))
                .Background(CardToneBrushes.CreateToneBrush(tone, alpha))
                .CornerRadius(8)
                .Padding(8)));

        return HStack(6, items.ToArray());
    }

    private Element BuildActionRow(bool miniChatOpen, bool flipped, bool canRefresh)
    {
        var actions = new List<Element>
        {
            Button("Inspect runtime", OnInspectRequested)
                .AutomationName($"Inspect runtime for {card.Title}")
                .SubtleButton(),
            Button(miniChatOpen ? "Hide chat" : card.ChatProcessing ? "Chat..." : "Chat", OnChatRequested)
                .AutomationName($"Chat controls for {card.Title}")
                .SubtleButton(),
        };

        if (canRefresh)
        {
            actions.Add(Button("Refresh", OnRefreshRequested)
                .AutomationName($"Refresh {card.Title}")
                .SubtleButton());
        }

        actions.Add(Button(flipped ? "Back to card" : "Flip to runtime", flipped ? OnShowFrontRequested : OnShowBackRequested)
            .AutomationName(flipped ? $"Show front of {card.Title}" : $"Show runtime details for {card.Title}")
            .SubtleButton());

        return HStack(8, actions.ToArray());
    }

    private void OnInspectRequested()
    {
        App.Current.BoardStore.SetInspectedCardId(card.Id);
    }

    private void OnChatRequested()
    {
        App app = App.Current;
        app.BoardStore.SetMiniChatOpen(card.Id, !app.BoardStore.IsMiniChatOpen(card.Id));
        requestRender();
    }

    private void OnShowFrontRequested()
    {
        App.Current.BoardStore.SetFlipped(card.Id, false);
        requestRender();
    }

    private void OnShowBackRequested()
    {
        App.Current.BoardStore.SetFlipped(card.Id, true);
        requestRender();
    }

    private void OnRefreshRequested()
    {
        _ = App.Current.BoardClient.RefreshCardAsync(card.Id);
    }
}