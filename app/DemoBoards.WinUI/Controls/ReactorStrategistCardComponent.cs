using System;
using System.Collections.Generic;
using DemoBoards.RuntimeHost;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorStrategistCardProps(BoardCard Card);

public sealed class ReactorStrategistCardComponent : Component<ReactorStrategistCardProps>
{
    public override Element Render()
    {
        BoardCard card = Props.Card;
        bool canRefresh = App.Current.BoardStore.GetCardState(card.Id)?.CanRefresh == true;

        var sections = new List<Element>();
        Element? pathStateBadge = BuildPathStateBadge(card);
        if (pathStateBadge is not null)
        {
            sections.Add(pathStateBadge);
        }

        if (canRefresh)
        {
            sections.Add(
                HStack(8,
                    TextBlock(string.Empty).Flex(grow: 1),
                    Button(string.Equals(card.Status, "running", StringComparison.OrdinalIgnoreCase) ? "Refreshing..." : "Refresh", () => _ = App.Current.BoardClient.RefreshCardAsync(card.Id))
                        .AutomationName($"Refresh {card.Title}")
                        .SubtleButton()
                        .Set(button => button.IsEnabled = !string.Equals(card.Status, "running", StringComparison.OrdinalIgnoreCase))));
        }

        sections.Add(Component<ReactorCardFrontContentComponent, ReactorCardFrontContentProps>(new ReactorCardFrontContentProps(card)));

        return Border(VStack(10, sections.ToArray()))
            .Padding(16)
            .Background(ReactorMainShellComponent.ResolveBrush("CardBackgroundFillColorDefaultBrush"))
            .WithBorder(CardToneBrushes.CreateToneBrush(card.Status, 0x66), 1)
            .CornerRadius(14);
    }

    private static Element? BuildPathStateBadge(BoardCard card)
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
}