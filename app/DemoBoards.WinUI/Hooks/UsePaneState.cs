using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UsePaneState — Reactor port of usePaneState.js
//
//  Owns a pane's data slice (the card ids matching the pane's include-filters, the active
//  card, and the nav-ready summaries) plus the pane's own UI state (rail open/closed and
//  the carousel index). It knows nothing about "should this pane exist" — presence is a
//  rendering consequence resolved by the caller, never a stored fact. filterCards is the
//  presence-free matching primitive (empty filters match nothing, exactly like the web).
// =====================================================================================

/// <summary>A single pane nav summary (port of usePaneState's <c>cards</c> entries).</summary>
public sealed record PaneCardSummary(
    string Id,
    IReadOnlyDictionary<string, string> Meta,
    IReadOnlyDictionary<string, string> CardData);

/// <summary>The pane state object returned by <see cref="HookComponent{TProps}.UsePaneState"/>.</summary>
public sealed record PaneState(
    BoardState Board,
    IReadOnlyList<string> CardIds,
    int Count,
    int Idx,
    string? ActiveCardId,
    IReadOnlyList<PaneCardSummary> Cards,
    bool Expanded,
    Action ToggleExpanded,
    Action GoPrev,
    Action GoNext);

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>usePaneState</c>: the pane's card slice plus rail/carousel UI state.</summary>
    protected PaneState UsePaneState(string boardId, IReadOnlyList<Func<BoardCardState, bool>>? includeFilters = null)
    {
        BoardState board = UseBoardState(boardId);
        var (expanded, setExpanded) = UseState(false);
        var (idx, setIdx) = UseState(0);

        // The web spreads the matched Set in insertion order, which equals the card-iteration
        // order from the board store. Preserve that order by walking board.CardIds (already in
        // store order) and keeping only the matched ones, instead of re-sorting the Set.
        IReadOnlySet<string> matched = board.FilterCards(includeFilters);
        IReadOnlyList<string> cardIds = board.CardIds
            .Where(cardId => matched.Contains(cardId))
            .ToArray();

        int count = cardIds.Count;
        int safeIdx = Math.Min(idx, Math.Max(0, count - 1));
        string? activeCardId = count > 0 && safeIdx >= 0 && safeIdx < count ? cardIds[safeIdx] : null;

        // Only materialize the nav summaries while the rail is open.
        var cards = new List<PaneCardSummary>();
        if (expanded)
        {
            foreach (string cardId in cardIds)
            {
                board.CardContents.TryGetValue(cardId, out BoardCard? cardContent);
                IReadOnlyDictionary<string, string> meta = cardContent?.MetaValues ?? EmptyDataObjects;
                IReadOnlyDictionary<string, string> cardData = cardContent is null
                    ? EmptyDataObjects
                    : cardContent.Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
                cards.Add(new PaneCardSummary(cardId, meta, cardData));
            }
        }

        void ToggleExpanded() => setExpanded(!expanded);
        void GoPrev() => setIdx(Math.Max(0, idx - 1));
        void GoNext() => setIdx(Math.Min(count - 1, idx + 1));

        return new PaneState(
            board,
            cardIds,
            count,
            safeIdx,
            activeCardId,
            cards,
            expanded,
            ToggleExpanded,
            GoPrev,
            GoNext);
    }
}
