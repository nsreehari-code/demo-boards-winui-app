using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseBoardState — Reactor port of useBoardState.js
//
//  Surfaces the whole-board data slice (info, status, data objects, card definitions and
//  runtimes, the refreshable-card set) plus the presence-free filtering primitives
//  (filterCards / excludedCards) and the board-level actions (initBoard / refreshAll).
//  In the embedded app there is no SSE: BoardStore already owns the snapshot in-process,
//  so the hook subscribes once and reads straight off the store; actions bind to
//  EmbeddedBoardClient. Components stay presentational.
// =====================================================================================

/// <summary>Board-level action callbacks (port of <c>useBoardState</c>'s memoized <c>boardActions</c>).</summary>
public sealed record BoardActions(
    Func<Task> InitBoard,
    Func<Task> RefreshAll);

/// <summary>The whole-board state object returned by <see cref="HookComponent{TProps}.UseBoardState"/>.</summary>
public sealed record BoardState(
    string BoardId,
    string SseClientId,
    BoardInfoState BoardInfo,
    IReadOnlyDictionary<string, BoardCard> CardContents,
    IReadOnlyDictionary<string, BoardCardRuntimeSlice> CardRuntimes,
    BoardSummaryState BoardStatus,
    IReadOnlyDictionary<string, string> DataObjects,
    IReadOnlyList<string> RefreshableCardIds,
    bool HasRefreshableCards,
    IReadOnlyList<string> CardIds,
    Func<IReadOnlyList<Func<BoardCardState, bool>>?, IReadOnlySet<string>> FilterCards,
    Func<IReadOnlyList<Func<BoardCardState, bool>>?, IReadOnlySet<string>> ExcludedCards,
    BoardActions BoardActions);

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useBoardState</c>: the resolved whole-board slice plus stable actions.</summary>
    protected BoardState UseBoardState(string boardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        EmbeddedBoardClient client = UseEmbeddedClient();

        BoardInfoState boardInfo = store.GetBoardInfo();
        BoardSummaryState boardStatus = store.State.Summary;
        IReadOnlyDictionary<string, string> dataObjects = store.State.DataObjectsByToken;
        IReadOnlyDictionary<string, BoardCard> cardContents = store.GetBoardCardDefinitionsAndData();
        IReadOnlyDictionary<string, BoardCardRuntimeSlice> cardRuntimes = store.GetBoardCardRuntimes();
        IReadOnlyList<string> cardIds = store.GetBoardCardIds();

        var refreshable = new List<string>();
        foreach (string cardId in cardIds)
        {
            if (cardContents.TryGetValue(cardId, out BoardCard? card) && ResolveCanRefresh(card))
            {
                refreshable.Add(cardId);
            }
        }

        IReadOnlyList<string> refreshableCardIds = refreshable;

        IReadOnlySet<string> FilterCards(IReadOnlyList<Func<BoardCardState, bool>>? filterFns)
        {
            Func<BoardCardState, bool>[] filters = (filterFns ?? Array.Empty<Func<BoardCardState, bool>>())
                .Where(filter => filter is not null)
                .ToArray();
            var matched = new HashSet<string>(StringComparer.Ordinal);
            foreach (string cardId in cardIds)
            {
                BoardCardState? cardState = store.GetCardState(cardId);
                if (cardState is null)
                {
                    continue;
                }

                if (filters.Any(filter => filter(cardState)))
                {
                    matched.Add(cardId);
                }
            }

            return matched;
        }

        IReadOnlySet<string> ExcludedCards(IReadOnlyList<Func<BoardCardState, bool>>? filterFns)
        {
            IReadOnlySet<string> matched = FilterCards(filterFns);
            var remaining = new HashSet<string>(StringComparer.Ordinal);
            foreach (string cardId in cardIds)
            {
                if (!matched.Contains(cardId))
                {
                    remaining.Add(cardId);
                }
            }

            return remaining;
        }

        var boardActions = new BoardActions(
            InitBoard: () => client.InitBoardAsync(),
            RefreshAll: () => Task.WhenAll(refreshableCardIds.Select(async cardId =>
            {
                // Promise.allSettled parity: an individual card refresh failure must not abort the batch.
                try
                {
                    await client.RefreshCardAsync(cardId).ConfigureAwait(false);
                }
                catch
                {
                    // swallowed, mirroring allSettled
                }
            })));

        return new BoardState(
            boardInfo.BoardId,
            boardInfo.ClientId,
            boardInfo,
            cardContents,
            cardRuntimes,
            boardStatus,
            dataObjects,
            refreshableCardIds,
            refreshableCardIds.Count > 0,
            cardIds,
            FilterCards,
            ExcludedCards,
            boardActions);
    }

    /// <summary>Port of <c>resolveCanRefresh</c>: a card can refresh when it declares source definitions.</summary>
    internal static bool ResolveCanRefresh(BoardCard? card) =>
        card is not null && card.SourceDefinitions.Count > 0;
}
