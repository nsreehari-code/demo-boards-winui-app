using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

/// <summary>
/// Mirrors <c>TemplateIngestPreview.jsx</c> — modal showing template ingest preview.
/// Displays cards to replace, add, and any invalid cards with detailed issues.
/// Props match frontend exactly: templateLabel, cardsToReplace, cardsToAdd, invalidCards, ingesting, onConfirm, onCancel.
/// </summary>
public sealed record TemplateIngestPreviewProps(
    string TemplateLabel = "",
    IReadOnlyList<object?>? CardsToReplace = null,
    IReadOnlyList<object?>? CardsToAdd = null,
    IReadOnlyList<object?>? InvalidCards = null,
    bool Ingesting = false,
    Action? OnConfirm = null,
    Action? OnCancel = null);

public sealed class TemplateIngestPreview : Component<TemplateIngestPreviewProps>
{
    private static string GetCardId(object? card) =>
        card is IDictionary<string, object?> dict && dict.TryGetValue("id", out var id)
            ? id?.ToString() ?? ""
            : "";

    private static string GetCardTitle(object? card) =>
        card is IDictionary<string, object?> dict && dict.TryGetValue("title", out var title)
            ? title?.ToString() ?? ""
            : "";

    private static IReadOnlyList<string> GetCardIssues(object? card)
    {
        if (card is IDictionary<string, object?> dict &&
            dict.TryGetValue("issues", out var issues) &&
            issues is IEnumerable<string> stringList)
        {
            return stringList.ToList();
        }
        if (card is IDictionary<string, object?> dict2 &&
            dict2.TryGetValue("issues", out var issues2) &&
            issues2 is IEnumerable<object?> objList)
        {
            return objList.Select(o => o?.ToString() ?? "").ToList();
        }
        return Array.Empty<string>();
    }

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var cardsToReplace = Props.CardsToReplace ?? Array.Empty<object?>();
        var cardsToAdd = Props.CardsToAdd ?? Array.Empty<object?>();
        var invalidCards = Props.InvalidCards ?? Array.Empty<object?>();
        bool hasInvalidCards = invalidCards.Count > 0;

        var sections = new List<Element>();

        // Template label
        sections.Add(
            TextBlock($"Template: {Props.TemplateLabel}")
                .FontSize(12)
                .Opacity(0.7));

        // Description
        sections.Add(
            TextBlock("This will upsert template cards into the current board. Existing cards with matching ids will be replaced. Board label, subtitle, and other board settings will not be changed.")
                .FontSize(12)
                .Set(tb => tb.TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap)
                .Opacity(0.8));

        // Summary badges
        var badges = new List<Element>
        {
            Border(TextBlock($"Replace: {cardsToReplace.Count}")
                .FontSize(11))
                .Padding(6, 4)
                .Background(theme.SurfaceElevated)
                .WithBorder(theme.CardBorder, 1),
            Border(TextBlock($"Add: {cardsToAdd.Count}")
                .FontSize(11))
                .Padding(6, 4)
                .Background(theme.SurfaceElevated)
                .WithBorder(theme.CardBorder, 1)
        };

        if (hasInvalidCards)
        {
            badges.Add(
                Border(TextBlock($"Invalid: {invalidCards.Count}")
                    .FontSize(11)
                    .Foreground(theme.StatusError))
                    .Padding(6, 4)
                    .Background(theme.SurfaceForTone("danger"))
                    .WithBorder(theme.StatusError, 1));
        }

        sections.Add(HStack(8, badges.ToArray()));

        // Invalid cards section
        if (hasInvalidCards)
        {
            var invalidCardElements = new List<Element>();
            foreach (var card in invalidCards)
            {
                string cardId = GetCardId(card);
                string cardTitle = GetCardTitle(card);
                var issues = GetCardIssues(card);

                string cardLabel = cardId;
                if (!string.IsNullOrEmpty(cardTitle))
                {
                    cardLabel += $" - {cardTitle}";
                }

                var issueElements = issues
                    .Select(issue => (Element)TextBlock(issue)
                        .FontSize(11)
                        .Foreground(theme.StatusError)
                        .Opacity(0.9))
                    .ToArray();

                invalidCardElements.Add(
                    VStack(2,
                        TextBlock(cardLabel)
                            .FontSize(12)
                            .Bold(),
                        VStack(0, issueElements)
                    ).Flex(grow: 1).Set(stack => stack.Padding = new(4)));
            }

            sections.Add(
                VStack(4,
                    TextBlock("Invalid cards")
                        .FontSize(12)
                        .Bold(),
                    VStack(4, invalidCardElements.ToArray())
                        .Set(stack =>
                        {
                            stack.Background = theme.SurfaceForTone("danger");
                            stack.BorderBrush = theme.StatusError;
                            stack.BorderThickness = new(1);
                            stack.Padding = new(8);
                        })
                ));
        }

        // Cards to replace section
        sections.Add(
            VStack(4,
                TextBlock("Cards to replace")
                    .FontSize(12)
                    .Bold(),
                cardsToReplace.Count == 0
                    ? (Element)TextBlock("None.")
                        .FontSize(11)
                        .Opacity(0.6)
                    : VStack(2, cardsToReplace
                        .Select(card =>
                        {
                            string cardId = GetCardId(card);
                            string cardTitle = GetCardTitle(card);
                            return (Element)TextBlock($"{cardId}" + (string.IsNullOrEmpty(cardTitle) ? "" : $" - {cardTitle}"))
                                .FontSize(11);
                        })
                        .ToArray())
            ));

        // Cards to add section
        sections.Add(
            VStack(4,
                TextBlock("Cards to add")
                    .FontSize(12)
                    .Bold(),
                cardsToAdd.Count == 0
                    ? (Element)TextBlock("None.")
                        .FontSize(11)
                        .Opacity(0.6)
                    : VStack(2, cardsToAdd
                        .Select(card =>
                        {
                            string cardId = GetCardId(card);
                            string cardTitle = GetCardTitle(card);
                            return (Element)TextBlock($"{cardId}" + (string.IsNullOrEmpty(cardTitle) ? "" : $" - {cardTitle}"))
                                .FontSize(11);
                        })
                        .ToArray())
            ));

        // Action buttons
        string confirmButtonText = hasInvalidCards
            ? "Fix Invalid Cards First"
            : (Props.Ingesting ? "Ingesting…" : "Go Ahead");

        var buttonSection = HStack(8,
            Button("Discard", Props.OnCancel ?? (() => { }))
                .AutomationName("Discard template ingestion")
                .SubtleButton(),
            Button(confirmButtonText, Props.OnConfirm ?? (() => { }))
                .IsEnabled(!Props.Ingesting && !hasInvalidCards)
                .AutomationName(hasInvalidCards ? "Fix invalid cards before proceeding" : (Props.Ingesting ? "Ingesting cards..." : "Confirm and ingest template cards"))
                .AccentButton()
        );

        sections.Add(buttonSection);

        return VStack(12, sections.ToArray())
            .Set(stack => stack.Padding = new(16));
    }
}
