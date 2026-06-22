using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DemoBoards.RuntimeHost;

namespace DemoBoards_WinUI.State;

public sealed record BoardInfoState(string BoardId, string ClientId);

public sealed record BoardCardState(
    string CardId,
    BoardCard? CardContent,
    bool CanRefresh,
    IReadOnlyDictionary<string, string> CardData,
    BoardCard? CardRuntime,
    IReadOnlyDictionary<string, string> RequiresDataObjects,
    IReadOnlyDictionary<string, string> ProvidesDataObjects);

public sealed record BoardSummaryState(
    int CardCount,
    int Pending,
    int InProgress,
    int Failed,
    int Completed);

public sealed record BoardCardDefinitionState(
    string Id,
    string Title,
    IReadOnlyDictionary<string, string> MetaValues,
    IReadOnlyList<BoardCardField> Fields,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Provides,
    IReadOnlyList<string> ViewKinds,
    IReadOnlyList<BoardRenderElement> ViewElements,
    IReadOnlyList<BoardSourceDefinition> SourceDefinitions,
    string RawDefinitionJson);

public sealed record BoardCardRuntimeSlice(
    string Status,
    IReadOnlyList<BoardCardField> ComputedValues,
    string RawRuntimeJson,
    string SchemaVersion);

public sealed record BoardCardChatViewState(
    IReadOnlyList<BoardChatMessage> Messages,
    bool Receiving,
    bool Processing);

public sealed record BoardStoreState(
    string BoardId,
    BoardSummaryState Summary,
    IReadOnlyDictionary<string, string> DataObjectsByToken,
    IReadOnlyDictionary<string, BoardCardDefinitionState> CardDefinitionsAndData,
    IReadOnlyDictionary<string, BoardCardRuntimeSlice> CardRuntimesById,
    IReadOnlyDictionary<string, BoardCardChatViewState> CardChatViews,
    IReadOnlyDictionary<string, BoardWatchpartyState> CardWatchParties,
    IReadOnlyDictionary<string, IReadOnlyList<BoardCardField>> PendingComputedValues,
    ManagedBoardConfigState? ManagedBoardConfig,
    BoardCanvasLayoutState CanvasLayout);

public sealed record BoardStoreChangedEventArgs(
    IReadOnlySet<string> ChangedCardIds,
    bool SummaryChanged,
    bool DataObjectsChanged,
    bool DefinitionsChanged,
    bool RuntimesChanged,
    bool ChatsChanged,
    bool WatchpartiesChanged,
    bool ManagedConfigChanged,
    bool LayoutChanged);

/// <summary>
/// WinUI equivalent of the frontend external-store + selector layer in
/// useSseSlices/useBoardState/useCardState. It is reducer-fed by the embedded
/// runtime service rather than HTTP SSE, but the dataflow shape is the same:
/// one shared board store with explicit slices and selector-style accessors.
/// </summary>
public sealed class BoardStore : IDisposable
{
    private readonly DemoBoardsRuntimeService runtimeService;
    private BoardStoreState state;
    private BoardSnapshot snapshot;
    private BoardUiState uiState = BoardUiState.Empty;

    public BoardStore(DemoBoardsRuntimeService runtimeService)
    {
        this.runtimeService = runtimeService;

        BoardSnapshot initialSnapshot = runtimeService.GetBoardSnapshot();
        state = BuildState(initialSnapshot, runtimeService.GetCardWatchparties());
        snapshot = BuildSnapshot(state);

        runtimeService.BoardSnapshotChanged += HandleSnapshotChanged;
        runtimeService.RuntimeNotificationsReceived += HandleRuntimeNotificationsReceived;
    }

    public event EventHandler<BoardSnapshot>? SnapshotChanged;
    public event EventHandler<BoardStoreChangedEventArgs>? StateChanged;
    public event EventHandler<BoardUiState>? UiStateChanged;

    public BoardSnapshot Snapshot => snapshot;
    public BoardStoreState State => state;
    public BoardUiState UiState => uiState;

    public BoardInfoState GetBoardInfo()
    {
        return new(state.BoardId, "embedded-v8");
    }

    public BoardCanvasLayoutState GetCanvasLayout()
    {
        return state.CanvasLayout;
    }

    public IReadOnlyDictionary<string, BoardCard> GetBoardCardDefinitionsAndData()
    {
        return GetBoardCardIds().Select(GetCardDefinitionAndData)
            .Where(card => card is not null)
            .ToDictionary(card => card!.Id, card => card!, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, BoardCard> GetBoardCardRuntimes()
    {
        return GetBoardCardDefinitionsAndData();
    }

    public IReadOnlyDictionary<string, string> GetRequiredDataObjects(BoardCard card)
    {
        if (card is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return card.Requires
            .Where(token => state.DataObjectsByToken.TryGetValue(token, out _))
            .ToDictionary(token => token, token => state.DataObjectsByToken[token], StringComparer.Ordinal);
    }

    public IReadOnlyList<string> GetBoardCardIds()
    {
        return state.CardDefinitionsAndData.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    public BoardCard? GetCardDefinitionAndData(string cardId)
    {
        return BuildBoardCard(cardId);
    }

    public BoardCard? GetCardRuntimeState(string cardId)
    {
        return BuildBoardCard(cardId);
    }

    public BoardWatchpartyState GetCardWatchparty(string cardId)
    {
        return state.CardWatchParties.TryGetValue(cardId, out BoardWatchpartyState? watchparty)
            ? watchparty
            : EmptyWatchparty();
    }

    public BoardCardState? GetCardState(string cardId)
    {
        BoardCard? card = GetCardDefinitionAndData(cardId);
        if (card is null) return null;

        var cardData = card.Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
        var requiresDataObjects = card.Requires
            .Where(token => state.DataObjectsByToken.TryGetValue(token, out _))
            .ToDictionary(token => token, token => state.DataObjectsByToken[token], StringComparer.Ordinal);
        var providesDataObjects = card.Provides
            .Where(token => state.DataObjectsByToken.TryGetValue(token, out _))
            .ToDictionary(token => token, token => state.DataObjectsByToken[token], StringComparer.Ordinal);

        return new BoardCardState(
            card.Id,
            card,
            card.SourceDefinitions.Count > 0,
            cardData,
            card,
            requiresDataObjects,
            providesDataObjects);
    }

    public IEnumerable<string> FilterCards(params Func<BoardCardState, bool>[] filterFns)
    {
        var filters = (filterFns ?? Array.Empty<Func<BoardCardState, bool>>()).Where(filter => filter is not null).ToArray();
        foreach (string cardId in GetBoardCardIds())
        {
            BoardCardState? cardState = GetCardState(cardId);
            if (cardState is null) continue;
            if (filters.Length == 0 || filters.Any(filter => filter(cardState)))
            {
                yield return cardId;
            }
        }
    }

    public IEnumerable<string> ExcludedCards(params Func<BoardCardState, bool>[] filterFns)
    {
        HashSet<string> excluded = new(FilterCards(filterFns), StringComparer.Ordinal);
        foreach (string cardId in GetBoardCardIds())
        {
            if (!excluded.Contains(cardId))
            {
                yield return cardId;
            }
        }
    }

    public void SetInspectedCardId(string? cardId)
    {
        string? normalized = string.IsNullOrWhiteSpace(cardId) ? null : cardId;
        if (uiState.InspectedCardId == normalized) return;
        uiState = uiState with { InspectedCardId = normalized };
        UiStateChanged?.Invoke(this, uiState);
    }

    public void SetFlipped(string cardId, bool isFlipped)
    {
        HashSet<string> next = new(uiState.FlippedCardIds, StringComparer.Ordinal);
        if (isFlipped) next.Add(cardId);
        else next.Remove(cardId);
        uiState = uiState with { FlippedCardIds = next };
        UiStateChanged?.Invoke(this, uiState);
    }

    public bool IsFlipped(string cardId)
    {
        return uiState.FlippedCardIds.Contains(cardId);
    }

    public void SetMiniChatOpen(string cardId, bool isOpen)
    {
        HashSet<string> next = new(uiState.MiniChatOpenCardIds, StringComparer.Ordinal);
        if (isOpen) next.Add(cardId);
        else next.Remove(cardId);
        uiState = uiState with { MiniChatOpenCardIds = next };
        UiStateChanged?.Invoke(this, uiState);
    }

    public bool IsMiniChatOpen(string cardId)
    {
        return uiState.MiniChatOpenCardIds.Contains(cardId);
    }

    public void SetManagedBoardConfig(ManagedBoardConfigState? config)
    {
        Dispatch(new SetManagedBoardConfigAction(config));
    }

    public void SetCanvasCardPosition(string cardId, double x, double y)
    {
        Dispatch(new SetCanvasCardPositionAction(cardId, x, y));
    }

    public void SetCanvasCardWidth(string cardId, double width)
    {
        Dispatch(new SetCanvasCardWidthAction(cardId, width));
    }

    public void SetCanvasViewport(double x, double y, double zoom)
    {
        Dispatch(new SetCanvasViewportAction(x, y, zoom));
    }

    private void HandleSnapshotChanged(object? sender, BoardSnapshot nextSnapshot)
    {
        Dispatch(new ReplacePublishedStateAction(nextSnapshot, runtimeService.GetCardWatchparties()));
    }

    private void HandleRuntimeNotificationsReceived(object? sender, string notificationsJson)
    {
        Dispatch(new ApplyRuntimeNotificationsAction(notificationsJson));
    }

    private void Dispatch(IBoardStoreAction action)
    {
        BoardStoreState previousState = state;
        BoardStoreState nextState = Reduce(state, action);
        if (ReferenceEquals(nextState, previousState))
        {
            return;
        }

        state = nextState;
        snapshot = BuildSnapshot(nextState);
        StateChanged?.Invoke(this, DescribeChange(previousState, nextState));
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static BoardStoreChangedEventArgs DescribeChange(BoardStoreState previousState, BoardStoreState nextState)
    {
        bool summaryChanged = !Equals(previousState.Summary, nextState.Summary);
        bool dataObjectsChanged = !ReferenceEquals(previousState.DataObjectsByToken, nextState.DataObjectsByToken);
        bool definitionsChanged = !ReferenceEquals(previousState.CardDefinitionsAndData, nextState.CardDefinitionsAndData);
        bool runtimesChanged = !ReferenceEquals(previousState.CardRuntimesById, nextState.CardRuntimesById);
        bool chatsChanged = !ReferenceEquals(previousState.CardChatViews, nextState.CardChatViews);
        bool watchpartiesChanged = !ReferenceEquals(previousState.CardWatchParties, nextState.CardWatchParties);
        bool managedConfigChanged = !Equals(previousState.ManagedBoardConfig, nextState.ManagedBoardConfig);
        bool layoutChanged = !Equals(previousState.CanvasLayout, nextState.CanvasLayout);

        HashSet<string> changedCardIds = new(StringComparer.Ordinal);
        AppendChangedKeys(changedCardIds, previousState.CardDefinitionsAndData, nextState.CardDefinitionsAndData);
        AppendChangedKeys(changedCardIds, previousState.CardRuntimesById, nextState.CardRuntimesById);
        AppendChangedKeys(changedCardIds, previousState.CardChatViews, nextState.CardChatViews);
        AppendChangedKeys(changedCardIds, previousState.CardWatchParties, nextState.CardWatchParties);
        AppendChangedKeys(changedCardIds, previousState.PendingComputedValues, nextState.PendingComputedValues);

        return new BoardStoreChangedEventArgs(
            changedCardIds,
            summaryChanged,
            dataObjectsChanged,
            definitionsChanged,
            runtimesChanged,
            chatsChanged,
            watchpartiesChanged,
            managedConfigChanged,
            layoutChanged);
    }

    private static void AppendChangedKeys<T>(HashSet<string> changedCardIds, IReadOnlyDictionary<string, T> previous, IReadOnlyDictionary<string, T> next)
    {
        foreach (string key in previous.Keys)
        {
            if (!next.TryGetValue(key, out T? nextValue) || !Equals(previous[key], nextValue))
            {
                changedCardIds.Add(key);
            }
        }

        foreach (string key in next.Keys)
        {
            if (!previous.TryGetValue(key, out T? previousValue) || !Equals(previousValue, next[key]))
            {
                changedCardIds.Add(key);
            }
        }
    }

    private BoardStoreState Reduce(BoardStoreState currentState, IBoardStoreAction action)
    {
        return action switch
        {
            ReplacePublishedStateAction replace => BuildState(replace.Snapshot, replace.Watchparties),
            ApplyRuntimeNotificationsAction apply => ApplyRuntimeNotifications(currentState, apply.NotificationsJson),
            SetManagedBoardConfigAction setManagedConfig => Equals(currentState.ManagedBoardConfig, setManagedConfig.Config)
                ? currentState
                : currentState with
                {
                    ManagedBoardConfig = setManagedConfig.Config,
                    CanvasLayout = ParseCanvasLayout(setManagedConfig.Config?.RawLayoutJson)
                },
            SetCanvasCardPositionAction setCanvasCardPosition => ReduceSetCanvasCardPosition(currentState, setCanvasCardPosition),
            SetCanvasCardWidthAction setCanvasCardWidth => ReduceSetCanvasCardWidth(currentState, setCanvasCardWidth),
            SetCanvasViewportAction setCanvasViewport => ReduceSetCanvasViewport(currentState, setCanvasViewport),
            _ => currentState,
        };
    }

    private static BoardStoreState ReduceSetCanvasCardPosition(BoardStoreState currentState, SetCanvasCardPositionAction action)
    {
        if (string.IsNullOrWhiteSpace(action.CardId))
        {
            return currentState;
        }

        BoardCanvasLayoutState currentLayout = currentState.CanvasLayout;
        if (currentLayout.Positions.TryGetValue(action.CardId, out BoardCanvasPointState? current)
            && current.X.Equals(action.X)
            && current.Y.Equals(action.Y))
        {
            return currentState;
        }

        var positions = new Dictionary<string, BoardCanvasPointState>(currentLayout.Positions, StringComparer.Ordinal)
        {
            [action.CardId] = new BoardCanvasPointState(action.X, action.Y)
        };
        IReadOnlyList<string> cardIds = currentLayout.CardIds.Contains(action.CardId, StringComparer.Ordinal)
            ? currentLayout.CardIds
            : currentLayout.CardIds.Concat(new[] { action.CardId }).ToArray();
        return currentState with
        {
            CanvasLayout = currentLayout with
            {
                CardIds = cardIds,
                Positions = positions,
            }
        };
    }

    private static BoardStoreState ReduceSetCanvasCardWidth(BoardStoreState currentState, SetCanvasCardWidthAction action)
    {
        if (string.IsNullOrWhiteSpace(action.CardId) || action.Width <= 0)
        {
            return currentState;
        }

        BoardCanvasLayoutState currentLayout = currentState.CanvasLayout;
        if (currentLayout.Widths.TryGetValue(action.CardId, out double currentWidth)
            && currentWidth.Equals(action.Width))
        {
            return currentState;
        }

        var widths = new Dictionary<string, double>(currentLayout.Widths, StringComparer.Ordinal)
        {
            [action.CardId] = action.Width
        };
        IReadOnlyList<string> cardIds = currentLayout.CardIds.Contains(action.CardId, StringComparer.Ordinal)
            ? currentLayout.CardIds
            : currentLayout.CardIds.Concat(new[] { action.CardId }).ToArray();
        return currentState with
        {
            CanvasLayout = currentLayout with
            {
                CardIds = cardIds,
                Widths = widths,
            }
        };
    }

    private static BoardStoreState ReduceSetCanvasViewport(BoardStoreState currentState, SetCanvasViewportAction action)
    {
        if (action.Zoom <= 0)
        {
            return currentState;
        }

        BoardCanvasViewportState nextViewport = new(action.X, action.Y, action.Zoom);
        if (Equals(currentState.CanvasLayout.Viewport, nextViewport))
        {
            return currentState;
        }

        return currentState with
        {
            CanvasLayout = currentState.CanvasLayout with { Viewport = nextViewport }
        };
    }

    private static BoardStoreState BuildState(BoardSnapshot snapshot, IReadOnlyDictionary<string, BoardWatchpartyState> watchparties)
    {
        var definitions = new Dictionary<string, BoardCardDefinitionState>(StringComparer.Ordinal);
        var runtimes = new Dictionary<string, BoardCardRuntimeSlice>(StringComparer.Ordinal);
        var chats = new Dictionary<string, BoardCardChatViewState>(StringComparer.Ordinal);

        foreach (BoardCard card in snapshot.Cards)
        {
            definitions[card.Id] = new BoardCardDefinitionState(
                card.Id,
                card.Title,
                card.MetaValues,
                card.Fields,
                card.Requires,
                card.Provides,
                card.ViewKinds,
                card.ViewElements,
                card.SourceDefinitions,
                card.RawDefinitionJson);
            runtimes[card.Id] = new BoardCardRuntimeSlice(
                card.Status,
                card.ComputedValues,
                card.RawRuntimeJson,
                card.SchemaVersion);
            chats[card.Id] = new BoardCardChatViewState(
                card.ChatMessages,
                card.ChatReceiving,
                card.ChatProcessing);
        }

        return new BoardStoreState(
            snapshot.BoardId,
            new BoardSummaryState(snapshot.CardCount, snapshot.Pending, snapshot.InProgress, snapshot.Failed, snapshot.Completed),
            new Dictionary<string, string>(snapshot.DataObjectsByToken, StringComparer.Ordinal),
            definitions,
            runtimes,
            chats,
            new Dictionary<string, BoardWatchpartyState>(watchparties, StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<BoardCardField>>(StringComparer.Ordinal),
            null,
            BoardCanvasLayoutState.Empty);
    }

    private static BoardSnapshot BuildSnapshot(BoardStoreState state)
    {
        return new BoardSnapshot(
            state.BoardId,
            state.Summary.CardCount,
            state.Summary.Pending,
            state.Summary.InProgress,
            state.Summary.Failed,
            state.Summary.Completed,
            state.DataObjectsByToken,
            state.CardDefinitionsAndData.Keys
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => BuildBoardCard(state, id))
                .Where(card => card is not null)
                .Select(card => card!)
                .ToArray());
    }

    private static BoardStoreState ApplyRuntimeNotifications(BoardStoreState currentState, string notificationsJson)
    {
        if (string.IsNullOrWhiteSpace(notificationsJson))
        {
            return currentState;
        }

        using JsonDocument document = JsonDocument.Parse(notificationsJson);
        JsonElement notifications = document.RootElement;
        if (notifications.ValueKind != JsonValueKind.Array)
        {
            return currentState;
        }

        string boardId = currentState.BoardId;
        BoardSummaryState summary = currentState.Summary;
        var dataObjects = new Dictionary<string, string>(currentState.DataObjectsByToken, StringComparer.Ordinal);
        var definitions = new Dictionary<string, BoardCardDefinitionState>(currentState.CardDefinitionsAndData, StringComparer.Ordinal);
        var runtimes = new Dictionary<string, BoardCardRuntimeSlice>(currentState.CardRuntimesById, StringComparer.Ordinal);
        var chats = new Dictionary<string, BoardCardChatViewState>(currentState.CardChatViews, StringComparer.Ordinal);
        var watchparties = new Dictionary<string, BoardWatchpartyState>(currentState.CardWatchParties, StringComparer.Ordinal);
        var pendingComputedValues = new Dictionary<string, IReadOnlyList<BoardCardField>>(currentState.PendingComputedValues, StringComparer.Ordinal);
        bool changed = false;

        foreach (JsonElement notification in notifications.EnumerateArray())
        {
            string kind = GetString(notification, "kind") ?? string.Empty;
            switch (kind)
            {
                case "card_chats":
                {
                    string? cardId = GetString(notification, "cardId");
                    if (string.IsNullOrWhiteSpace(cardId))
                    {
                        break;
                    }

                    BoardCardChatViewState currentChat = chats.TryGetValue(cardId, out BoardCardChatViewState? chatView)
                        ? chatView
                        : EmptyChatSlice();
                    BoardCardChatViewState nextChat = new(
                        ParseNotificationMessages(TryGetArray(notification, "messages")),
                        GetBool(notification, "receiving", currentChat.Receiving),
                        GetBool(notification, "processing", currentChat.Processing));
                    if (!ChatStatesEqual(currentChat, nextChat))
                    {
                        chats[cardId] = nextChat;
                        changed = true;
                    }

                    break;
                }

                case "chat_messages":
                {
                    string? cardId = GetString(notification, "cardId");
                    if (string.IsNullOrWhiteSpace(cardId))
                    {
                        break;
                    }

                    BoardCardChatViewState currentChat = chats.TryGetValue(cardId, out BoardCardChatViewState? chatView)
                        ? chatView
                        : EmptyChatSlice();
                    BoardCardChatViewState nextChat = currentChat with
                    {
                        Messages = ParseNotificationMessages(TryGetArray(notification, "messages")),
                    };
                    if (!ChatStatesEqual(currentChat, nextChat))
                    {
                        chats[cardId] = nextChat;
                        changed = true;
                    }

                    break;
                }

                case "chat_processing":
                {
                    string? cardId = GetString(notification, "cardId");
                    if (string.IsNullOrWhiteSpace(cardId))
                    {
                        break;
                    }

                    BoardCardChatViewState currentChat = chats.TryGetValue(cardId, out BoardCardChatViewState? chatView)
                        ? chatView
                        : EmptyChatSlice();
                    BoardCardChatViewState nextChat = currentChat with
                    {
                        Processing = GetBool(notification, "active", currentChat.Processing),
                    };
                    if (!ChatStatesEqual(currentChat, nextChat))
                    {
                        chats[cardId] = nextChat;
                        changed = true;
                    }

                    break;
                }

                case "card_watchparty":
                {
                    string? cardId = GetString(notification, "cardId");
                    string? channel = GetString(notification, "channel");
                    if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(channel))
                    {
                        break;
                    }

                    BoardWatchpartyState currentWatchparty = watchparties.TryGetValue(cardId, out BoardWatchpartyState? state)
                        ? state
                        : EmptyWatchparty();
                    BoardWatchpartyState nextWatchparty = ApplyWatchpartyNotification(
                        currentWatchparty,
                        cardId,
                        channel,
                        notification.TryGetProperty("payload", out JsonElement payloadElement) ? payloadElement : default,
                        GetBool(notification, "clear", false),
                        GetBool(notification, "replace", false));
                    if (!WatchpartyStatesEqual(currentWatchparty, nextWatchparty))
                    {
                        watchparties[cardId] = nextWatchparty;
                        changed = true;
                    }

                    break;
                }

                case "computed_values":
                {
                    string? cardId = GetString(notification, "cardId");
                    if (string.IsNullOrWhiteSpace(cardId))
                    {
                        break;
                    }

                    IReadOnlyList<BoardCardField> values = ParseFields(TryGetObject(notification, "values"));
                    if (runtimes.TryGetValue(cardId, out BoardCardRuntimeSlice? runtime))
                    {
                        if (!FieldListsEqual(runtime.ComputedValues, values))
                        {
                            runtimes[cardId] = runtime with { ComputedValues = values };
                            pendingComputedValues.Remove(cardId);
                            changed = true;
                        }
                    }
                    else if (!pendingComputedValues.TryGetValue(cardId, out IReadOnlyList<BoardCardField>? pending)
                        || !FieldListsEqual(pending, values))
                    {
                        pendingComputedValues[cardId] = values;
                        changed = true;
                    }

                    break;
                }

                case "data_object":
                {
                    string? key = GetString(notification, "key");
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        break;
                    }

                    string payload = RenderValue(notification.TryGetProperty("payload", out JsonElement payloadElement)
                        ? payloadElement
                        : default);
                    if (!dataObjects.TryGetValue(key, out string? previous) || !string.Equals(previous, payload, StringComparison.Ordinal))
                    {
                        dataObjects[key] = payload;
                        changed = true;
                    }

                    break;
                }

                case "status":
                {
                    JsonElement status = TryGetObject(notification, "status");
                    BoardSummaryState nextSummary = ParseSummary(status, summary);
                    if (!Equals(nextSummary, summary))
                    {
                        summary = nextSummary;
                        changed = true;
                    }

                    JsonElement cards = TryGetArray(status, "cards");
                    if (cards.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement statusCard in cards.EnumerateArray())
                        {
                            string? cardId = GetString(statusCard, "name");
                            if (string.IsNullOrWhiteSpace(cardId) || !runtimes.TryGetValue(cardId, out BoardCardRuntimeSlice? runtime))
                            {
                                continue;
                            }

                            string nextStatus = GetString(statusCard, "status") ?? runtime.Status;
                            string nextRawRuntimeJson = runtime.RawRuntimeJson;
                            if (statusCard.TryGetProperty("runtime", out JsonElement runtimeElement)
                                && runtimeElement.ValueKind == JsonValueKind.Object)
                            {
                                nextRawRuntimeJson = runtimeElement.GetRawText();
                            }

                            if (!string.Equals(runtime.Status, nextStatus, StringComparison.Ordinal)
                                || !string.Equals(runtime.RawRuntimeJson, nextRawRuntimeJson, StringComparison.Ordinal))
                            {
                                runtimes[cardId] = runtime with
                                {
                                    Status = nextStatus,
                                    RawRuntimeJson = nextRawRuntimeJson,
                                };
                                changed = true;
                            }
                        }
                    }

                    break;
                }

                case "card_refreshed":
                {
                    string? cardId = GetString(notification, "cardId");
                    JsonElement cardElement = TryGetObject(notification, "card");
                    if (string.IsNullOrWhiteSpace(cardId) || cardElement.ValueKind != JsonValueKind.Object)
                    {
                        break;
                    }

                    BoardCardDefinitionState definition = ParseDefinition(cardElement, cardId);
                    definitions[cardId] = definition;
                    if (!runtimes.ContainsKey(cardId))
                    {
                        runtimes[cardId] = EmptyRuntimeSlice();
                    }
                    if (!chats.ContainsKey(cardId))
                    {
                        chats[cardId] = EmptyChatSlice();
                    }
                    if (pendingComputedValues.TryGetValue(cardId, out IReadOnlyList<BoardCardField>? pendingValues))
                    {
                        BoardCardRuntimeSlice runtime = runtimes[cardId];
                        runtimes[cardId] = runtime with { ComputedValues = pendingValues };
                        pendingComputedValues.Remove(cardId);
                    }

                    summary = summary with { CardCount = definitions.Count };
                    changed = true;
                    break;
                }

                case "card_removed":
                {
                    string? cardId = GetString(notification, "cardId");
                    if (string.IsNullOrWhiteSpace(cardId) || !definitions.ContainsKey(cardId))
                    {
                        break;
                    }

                    definitions.Remove(cardId);
                    runtimes.Remove(cardId);
                    chats.Remove(cardId);
                    watchparties.Remove(cardId);
                    pendingComputedValues.Remove(cardId);
                    summary = summary with { CardCount = definitions.Count };
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return currentState;
        }

        return new BoardStoreState(
            boardId,
            summary,
            dataObjects,
            definitions,
            runtimes,
            chats,
            watchparties,
            pendingComputedValues,
            currentState.ManagedBoardConfig,
            currentState.CanvasLayout);
    }

    private static BoardCanvasLayoutState ParseCanvasLayout(string? rawLayoutJson)
    {
        if (string.IsNullOrWhiteSpace(rawLayoutJson))
        {
            return BoardCanvasLayoutState.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawLayoutJson);
            JsonElement root = document.RootElement;
            JsonElement candidate = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("canvas", out JsonElement canvas)
                && canvas.ValueKind == JsonValueKind.Object
                ? canvas
                : root;
            if (candidate.ValueKind != JsonValueKind.Object)
            {
                return BoardCanvasLayoutState.Empty;
            }

            var positions = new Dictionary<string, BoardCanvasPointState>(StringComparer.Ordinal);
            if (candidate.TryGetProperty("positions", out JsonElement positionsElement)
                && positionsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in positionsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    double? x = GetDouble(property.Value, "x");
                    double? y = GetDouble(property.Value, "y");
                    if (x is null || y is null)
                    {
                        continue;
                    }

                    positions[property.Name] = new BoardCanvasPointState(x.Value, y.Value);
                }
            }

            var widths = new Dictionary<string, double>(StringComparer.Ordinal);
            if (candidate.TryGetProperty("widths", out JsonElement widthsElement)
                && widthsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in widthsElement.EnumerateObject())
                {
                    double? width = property.Value.ValueKind == JsonValueKind.Number ? property.Value.GetDouble() : null;
                    if (width is not null && width.Value > 0)
                    {
                        widths[property.Name] = width.Value;
                    }
                }
            }

            string[] cardIds = candidate.TryGetProperty("cardIds", out JsonElement cardIdsElement)
                && cardIdsElement.ValueKind == JsonValueKind.Array
                ? cardIdsElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToArray()
                : positions.Keys.ToArray();

            BoardCanvasViewportState? viewport = null;
            if (candidate.TryGetProperty("viewport", out JsonElement viewportElement)
                && viewportElement.ValueKind == JsonValueKind.Object)
            {
                double? x = GetDouble(viewportElement, "x");
                double? y = GetDouble(viewportElement, "y");
                double? zoom = GetDouble(viewportElement, "zoom");
                if (x is not null && y is not null && zoom is not null)
                {
                    viewport = new BoardCanvasViewportState(x.Value, y.Value, zoom.Value);
                }
            }

            return new BoardCanvasLayoutState(cardIds, positions, widths, viewport);
        }
        catch
        {
            return BoardCanvasLayoutState.Empty;
        }
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.GetDouble();
    }

    private BoardCard? BuildBoardCard(string cardId)
    {
        return BuildBoardCard(state, cardId);
    }

    private static BoardCard? BuildBoardCard(BoardStoreState currentState, string cardId)
    {
        if (!currentState.CardDefinitionsAndData.TryGetValue(cardId, out BoardCardDefinitionState? definition))
        {
            return null;
        }

        BoardCardRuntimeSlice runtime = currentState.CardRuntimesById.TryGetValue(cardId, out BoardCardRuntimeSlice? runtimeState)
            ? runtimeState
            : EmptyRuntimeSlice();
        BoardCardChatViewState chat = currentState.CardChatViews.TryGetValue(cardId, out BoardCardChatViewState? chatView)
            ? chatView
            : EmptyChatSlice();

        return new BoardCard(
            definition.Id,
            definition.Title,
            runtime.Status,
            definition.MetaValues,
            definition.Fields,
            runtime.ComputedValues,
            definition.Requires,
            definition.Provides,
            definition.ViewKinds,
            definition.ViewElements,
            definition.SourceDefinitions,
            chat.Messages,
            chat.Receiving,
            chat.Processing,
            definition.RawDefinitionJson,
            runtime.RawRuntimeJson,
            runtime.SchemaVersion);
    }

    private static BoardSummaryState ParseSummary(JsonElement status, BoardSummaryState fallback)
    {
        JsonElement summary = TryGetObject(status, "summary");
        if (summary.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        return new BoardSummaryState(
            GetInt(summary, "card_count", fallback.CardCount),
            GetInt(summary, "pending", fallback.Pending),
            GetInt(summary, "in_progress", fallback.InProgress),
            GetInt(summary, "failed", fallback.Failed),
            GetInt(summary, "completed", fallback.Completed));
    }

    private static BoardCardDefinitionState ParseDefinition(JsonElement definition, string fallbackCardId)
    {
        string cardId = GetString(definition, "id") ?? fallbackCardId;
        JsonElement cardData = TryGetObject(definition, "card_data");
        JsonElement meta = TryGetObject(definition, "meta");
        string title = GetString(cardData, "title") ?? GetString(meta, "title") ?? cardId;

        var fields = new List<BoardCardField>();
        if (cardData.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in cardData.EnumerateObject())
            {
                if (property.NameEquals("title") || property.NameEquals("requires") || property.NameEquals("provides"))
                {
                    continue;
                }

                fields.Add(new BoardCardField(property.Name, RenderValue(property.Value)));
            }
        }

        return new BoardCardDefinitionState(
            cardId,
            title,
            ParseStringDictionary(meta),
            fields,
            ParseTokenList(cardData, "requires"),
            ParseProvideList(cardData),
            ParseViewKinds(definition),
            ParseViewElements(definition),
            ParseSourceDefinitions(definition),
            definition.GetRawText());
    }

    private static IReadOnlyDictionary<string, string> ParseStringDictionary(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            result[property.Name] = RenderValue(property.Value);
        }

        return result;
    }

    private static IReadOnlyList<BoardCardField> ParseFields(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<BoardCardField>();
        }

        var fields = new List<BoardCardField>();
        foreach (JsonProperty property in value.EnumerateObject())
        {
            fields.Add(new BoardCardField(property.Name, RenderValue(property.Value)));
        }

        return fields;
    }

    private static IReadOnlyList<string> ParseTokenList(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out JsonElement value))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in value.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    AddToken(tokens, entry.GetString());
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in value.EnumerateObject())
            {
                AddToken(tokens, entry.Name);
            }
        }

        return tokens;
    }

    private static IReadOnlyList<string> ParseProvideList(JsonElement parent)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty("provides", out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                AddToken(tokens, entry.GetString());
            }
            else if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("bindTo", out JsonElement bindTo)
                && bindTo.ValueKind == JsonValueKind.String)
            {
                AddToken(tokens, bindTo.GetString());
            }
        }

        return tokens;
    }

    private static IReadOnlyList<string> ParseViewKinds(JsonElement definition)
    {
        return ParseViewElements(definition).Select(element => element.Kind).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<BoardRenderElement> ParseViewElements(JsonElement definition)
    {
        JsonElement view = TryGetObject(definition, "view");
        if (view.ValueKind != JsonValueKind.Object
            || !view.TryGetProperty("elements", out JsonElement elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BoardRenderElement>();
        }

        var parsed = new List<BoardRenderElement>();
        foreach (JsonElement element in elements.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("kind", out JsonElement kindElement)
                && kindElement.ValueKind == JsonValueKind.String)
            {
                parsed.Add(new BoardRenderElement(
                    kindElement.GetString() ?? string.Empty,
                    GetString(element, "label") ?? string.Empty,
                    GetString(element, "className") ?? string.Empty,
                    GetString(element, "visible") ?? string.Empty,
                    element.GetRawText()));
            }
        }

        return parsed;
    }

    private static IReadOnlyList<BoardSourceDefinition> ParseSourceDefinitions(JsonElement definition)
    {
        if (!definition.TryGetProperty("source_defs", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BoardSourceDefinition>();
        }

        var definitions = new List<BoardSourceDefinition>();
        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string bindTo = GetString(source, "bindTo") ?? string.Empty;
            var detailFields = new List<BoardCardField>();
            foreach (JsonProperty property in source.EnumerateObject())
            {
                if (property.NameEquals("bindTo") || property.Name.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                detailFields.Add(new BoardCardField(property.Name, RenderValue(property.Value)));
            }

            definitions.Add(new BoardSourceDefinition(bindTo, detailFields));
        }

        return definitions;
    }

    private static void AddToken(List<string> tokens, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token) && !tokens.Contains(token, StringComparer.Ordinal))
        {
            tokens.Add(token);
        }
    }

    private static IReadOnlyList<BoardChatMessage> ParseNotificationMessages(JsonElement messages)
    {
        if (messages.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BoardChatMessage>();
        }

        var parsed = new List<BoardChatMessage>();
        foreach (JsonElement message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            parsed.Add(new BoardChatMessage(
                GetString(message, "role") ?? "system",
                GetString(message, "text") ?? string.Empty,
                GetString(message, "turn") ?? string.Empty,
                GetBool(message, "processing", false)));
        }

        return parsed;
    }

    private static BoardWatchpartyState ApplyWatchpartyNotification(
        BoardWatchpartyState current,
        string cardId,
        string channel,
        JsonElement payload,
        bool clear,
        bool replace)
    {
        string normalizedChannel = channel.Trim();
        BoardWatchpartyState cleared = clear || replace
            ? ClearWatchpartyChannel(current, normalizedChannel)
            : current;
        BoardWatchpartyState delta = ParseWatchpartyDelta(cardId, normalizedChannel, payload);

        if (string.Equals(normalizedChannel, "agent-output", StringComparison.Ordinal))
        {
            if (replace || !string.IsNullOrWhiteSpace(delta.AgentOutput) || clear)
            {
                return cleared with { AgentOutput = delta.AgentOutput };
            }

            return current;
        }

        if (string.Equals(normalizedChannel, "agent-tools", StringComparison.Ordinal))
        {
            if (replace || clear)
            {
                return cleared with
                {
                    AgentTools = delta.AgentTools,
                    AgentToolPayloads = delta.AgentToolPayloads,
                };
            }

            return cleared with
            {
                AgentTools = AppendWatchpartyText(cleared.AgentTools, delta.AgentTools),
                AgentToolPayloads = AppendWatchpartyToolPayloads(cleared.AgentToolPayloads, delta.AgentToolPayloads),
            };
        }

        return current;
    }

    private static BoardWatchpartyState ClearWatchpartyChannel(BoardWatchpartyState state, string channel)
    {
        if (string.Equals(channel, "agent-output", StringComparison.Ordinal))
        {
            return state with { AgentOutput = string.Empty };
        }

        if (string.Equals(channel, "agent-tools", StringComparison.Ordinal))
        {
            return state with
            {
                AgentTools = string.Empty,
                AgentToolPayloads = Array.Empty<BoardWatchpartyToolPayload>(),
            };
        }

        return state;
    }

    private static BoardWatchpartyState ParseWatchpartyDelta(string cardId, string channel, JsonElement payload)
    {
        string payloadJson = payload.ValueKind is JsonValueKind.Undefined
            ? "null"
            : payload.GetRawText();
        string cardIdJson = JsonSerializer.Serialize(cardId);
        string channelJson = JsonSerializer.Serialize(channel);
        string watchpartyJson = "{\"cardWatchParties\":{" + cardIdJson + ":{" + channelJson + ":[{\"payload\":" + payloadJson + "}]}}}";

        return BoardSnapshot.ParseWatchparties(watchpartyJson).TryGetValue(cardId, out BoardWatchpartyState? watchparty)
            ? watchparty
            : EmptyWatchparty();
    }

    private static IReadOnlyList<BoardWatchpartyToolPayload> AppendWatchpartyToolPayloads(
        IReadOnlyList<BoardWatchpartyToolPayload> current,
        IReadOnlyList<BoardWatchpartyToolPayload> delta)
    {
        if (delta.Count == 0)
        {
            return current;
        }

        var merged = new List<BoardWatchpartyToolPayload>(current);
        foreach (BoardWatchpartyToolPayload payload in delta)
        {
            if (!merged.Contains(payload))
            {
                merged.Add(payload);
            }
        }

        return merged;
    }

    private static string AppendWatchpartyText(string current, string delta)
    {
        if (string.IsNullOrWhiteSpace(delta))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return delta;
        }

        return current.Contains(delta, StringComparison.Ordinal)
            ? current
            : current + "\n" + delta;
    }

    private static string RenderValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => "null",
            _ => value.GetRawText()
        };
    }

    private static JsonElement TryGetObject(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out JsonElement value))
        {
            return value;
        }

        return default;
    }

    private static JsonElement TryGetArray(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        return default;
    }

    private static string? GetString(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static bool GetBool(JsonElement parent, string property, bool fallback)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return fallback;
    }

    private static int GetInt(JsonElement parent, string property, int fallback)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result))
        {
            return result;
        }

        return fallback;
    }

    private static bool FieldListsEqual(IReadOnlyList<BoardCardField> left, IReadOnlyList<BoardCardField> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index += 1)
        {
            if (!string.Equals(left[index].Key, right[index].Key, StringComparison.Ordinal)
                || !string.Equals(left[index].Value, right[index].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ChatStatesEqual(BoardCardChatViewState left, BoardCardChatViewState right)
    {
        if (left.Receiving != right.Receiving || left.Processing != right.Processing || left.Messages.Count != right.Messages.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Messages.Count; index += 1)
        {
            if (!Equals(left.Messages[index], right.Messages[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool WatchpartyStatesEqual(BoardWatchpartyState left, BoardWatchpartyState right)
    {
        if (!string.Equals(left.AgentOutput, right.AgentOutput, StringComparison.Ordinal)
            || !string.Equals(left.AgentTools, right.AgentTools, StringComparison.Ordinal)
            || left.AgentToolPayloads.Count != right.AgentToolPayloads.Count)
        {
            return false;
        }

        for (int index = 0; index < left.AgentToolPayloads.Count; index += 1)
        {
            if (!Equals(left.AgentToolPayloads[index], right.AgentToolPayloads[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static BoardCardRuntimeSlice EmptyRuntimeSlice()
    {
        return new BoardCardRuntimeSlice("fresh", Array.Empty<BoardCardField>(), "{}", string.Empty);
    }

    private static BoardCardChatViewState EmptyChatSlice()
    {
        return new BoardCardChatViewState(Array.Empty<BoardChatMessage>(), false, false);
    }

    private static BoardWatchpartyState EmptyWatchparty()
    {
        return new BoardWatchpartyState(string.Empty, string.Empty, Array.Empty<BoardWatchpartyToolPayload>());
    }

    public void Dispose()
    {
        runtimeService.BoardSnapshotChanged -= HandleSnapshotChanged;
        runtimeService.RuntimeNotificationsReceived -= HandleRuntimeNotificationsReceived;
    }

    private interface IBoardStoreAction;

    private sealed record ReplacePublishedStateAction(
        BoardSnapshot Snapshot,
        IReadOnlyDictionary<string, BoardWatchpartyState> Watchparties) : IBoardStoreAction;

    private sealed record ApplyRuntimeNotificationsAction(string NotificationsJson) : IBoardStoreAction;

    private sealed record SetManagedBoardConfigAction(ManagedBoardConfigState? Config) : IBoardStoreAction;

    private sealed record SetCanvasCardPositionAction(string CardId, double X, double Y) : IBoardStoreAction;

    private sealed record SetCanvasCardWidthAction(string CardId, double Width) : IBoardStoreAction;

    private sealed record SetCanvasViewportAction(double X, double Y, double Zoom) : IBoardStoreAction;
}
