(function configureWinuiBoardSseReducerHost(global) {
  'use strict';

  const state = {
    snapshot: null,
    changes: {
      summaryChanged: true,
      dataObjectsChanged: true,
      definitionsChanged: true,
      runtimesChanged: true,
      computedValuesChanged: true,
      chatsChanged: true,
      watchpartiesChanged: true,
    },
  };

  function parseJsonSafely(jsonText, fallback) {
    if (typeof jsonText !== 'string' || jsonText.trim().length === 0) {
      return fallback;
    }

    try {
      return JSON.parse(jsonText);
    } catch {
      return fallback;
    }
  }

  function cloneJson(value) {
    return JSON.parse(JSON.stringify(value));
  }

  function ensureReducerSnapshot(snapshot) {
    if (snapshot && typeof snapshot === 'object') {
      return snapshot;
    }

    return global.BoardSseState.createEmptyBoardSnapshot(null);
  }

  function toNotificationFrame(parsed) {
    if (Array.isArray(parsed)) {
      return {
        kind: 'notification-batch',
        notifications: parsed,
      };
    }

    if (parsed && typeof parsed === 'object') {
      return parsed;
    }

    return {
      kind: 'notification-batch',
      notifications: [],
    };
  }

  function toLegacyPublishedPayload(snapshotLike) {
    const snapshot = ensureReducerSnapshot(snapshotLike);
    const boardId = snapshot?.boardInfo?.boardId ?? 'unknown-board';
    const definitionsAndData = snapshot?.cardDefinitionsAndData ?? {};
    const runtimes = snapshot?.boardStatus?.cardRuntimesById ?? {};
    const computedValuesById = snapshot?.boardCardComputedValues ?? {};
    const chatsById = snapshot?.cardChatViews ?? {};
    const watchPartiesById = snapshot?.cardWatchParties ?? {};

    const cardIdSet = new Set([
      ...Object.keys(definitionsAndData),
      ...Object.keys(runtimes),
      ...Object.keys(computedValuesById),
      ...Object.keys(chatsById),
      ...Object.keys(watchPartiesById),
    ]);
    const cardIds = Array.from(cardIdSet).sort();

    const cardDefinitions = [];
    const cardRuntimeById = {};
    const statusCards = [];
    const cardChatsByCardId = {};

    for (const cardId of cardIds) {
      const definitionAndData = definitionsAndData[cardId] ?? {};
      const cardContent = definitionAndData?.cardContent && typeof definitionAndData.cardContent === 'object'
        ? cloneJson(definitionAndData.cardContent)
        : { id: cardId, card_data: {} };
      if (!cardContent.id) {
        cardContent.id = cardId;
      }

      const cardData = definitionAndData?.cardData && typeof definitionAndData.cardData === 'object'
        ? definitionAndData.cardData
        : {};
      const contentCardData = cardContent?.card_data && typeof cardContent.card_data === 'object'
        ? cardContent.card_data
        : {};
      cardContent.card_data = {
        ...contentCardData,
        ...cardData,
      };
      cardDefinitions.push(cardContent);

      const runtimeEntry = runtimes[cardId] ?? {};
      const runtime = runtimeEntry?.runtime && typeof runtimeEntry.runtime === 'object'
        ? runtimeEntry.runtime
        : {};
      const status = typeof runtimeEntry?.status === 'string'
        ? runtimeEntry.status
        : 'fresh';
      const computedValues = computedValuesById[cardId] && typeof computedValuesById[cardId] === 'object'
        ? computedValuesById[cardId]
        : {};
      cardRuntimeById[cardId] = {
        status,
        runtime,
        computed_values: computedValues,
      };
      statusCards.push({
        name: cardId,
        status,
        runtime,
      });

      const chatState = chatsById?.[cardId]?.chatState;
      if (chatState && typeof chatState === 'object') {
        cardChatsByCardId[cardId] = {
          messages: Array.isArray(chatState.messages) ? chatState.messages : [],
          receiving: !!chatState.receiving,
          processing: !!chatState.processing,
        };
      }
    }

    const summary = snapshot?.boardStatus?.summary && typeof snapshot.boardStatus.summary === 'object'
      ? snapshot.boardStatus.summary
      : {};

    return {
      boardId,
      dataObjectsByToken: snapshot?.boardDataObjects ?? {},
      cardDefinitions,
      cardRuntimeById,
      statusSnapshot: {
        summary: {
          card_count: Number.isFinite(Number(summary.card_count)) ? Number(summary.card_count) : cardDefinitions.length,
          pending: Number.isFinite(Number(summary.pending)) ? Number(summary.pending) : 0,
          in_progress: Number.isFinite(Number(summary.in_progress)) ? Number(summary.in_progress) : 0,
          failed: Number.isFinite(Number(summary.failed)) ? Number(summary.failed) : 0,
          completed: Number.isFinite(Number(summary.completed)) ? Number(summary.completed) : 0,
        },
        cards: statusCards,
      },
      cardChatsByCardId,
      cardWatchParties: watchPartiesById,
    };
  }

  function toCanonicalEnvelope(snapshotLike) {
    const snapshot = ensureReducerSnapshot(snapshotLike);
    return {
      boardId: snapshot?.boardInfo?.boardId ?? 'unknown-board',
      summary: snapshot?.boardStatus?.summary ?? null,
      dataObjectsByToken: snapshot?.boardDataObjects ?? {},
      cardDefinitionsAndData: snapshot?.cardDefinitionsAndData ?? {},
      cardRuntimesById: snapshot?.boardStatus?.cardRuntimesById ?? {},
      boardCardComputedValues: snapshot?.boardCardComputedValues ?? {},
      cardChatViews: snapshot?.cardChatViews ?? {},
      cardWatchParties: snapshot?.cardWatchParties ?? {},
      changes: {
        ...state.changes,
      },
    };
  }

  function captureChanges(previous, next) {
    state.changes = {
      summaryChanged: previous?.boardStatus?.summary !== next?.boardStatus?.summary,
      dataObjectsChanged: previous?.boardDataObjects !== next?.boardDataObjects,
      definitionsChanged: previous?.cardDefinitionsAndData !== next?.cardDefinitionsAndData,
      runtimesChanged: previous?.boardStatus?.cardRuntimesById !== next?.boardStatus?.cardRuntimesById,
      computedValuesChanged: previous?.boardCardComputedValues !== next?.boardCardComputedValues,
      chatsChanged: previous?.cardChatViews !== next?.cardChatViews,
      watchpartiesChanged: previous?.cardWatchParties !== next?.cardWatchParties,
    };
  }

  function applyFrame(frame) {
    const previous = state.snapshot;
    state.snapshot = ensureReducerSnapshot(global.BoardSseState.applyBoardSseFrame(state.snapshot, frame));
    captureChanges(previous, state.snapshot);
    return JSON.stringify(toLegacyPublishedPayload(state.snapshot));
  }

  global.winuiBoardSseReducerInitFromPublishedPayload = function winuiBoardSseReducerInitFromPublishedPayload(publishedPayloadJson) {
    const payload = parseJsonSafely(publishedPayloadJson, null);
    const boardId = payload && typeof payload === 'object' ? payload.boardId ?? null : null;
    const previous = state.snapshot;
    state.snapshot = global.BoardSseState.createEmptyBoardSnapshot(boardId);
    if (payload && typeof payload === 'object') {
      state.snapshot = ensureReducerSnapshot(global.BoardSseState.applyBoardSseFrame(state.snapshot, payload));
    }

    captureChanges(previous, state.snapshot);

    return JSON.stringify(toLegacyPublishedPayload(state.snapshot));
  };

  global.winuiBoardSseReducerReplacePublishedPayload = function winuiBoardSseReducerReplacePublishedPayload(publishedPayloadJson) {
    return global.winuiBoardSseReducerInitFromPublishedPayload(publishedPayloadJson);
  };

  global.winuiBoardSseReducerApplyNotifications = function winuiBoardSseReducerApplyNotifications(notificationsJson) {
    if (!state.snapshot) {
      state.snapshot = global.BoardSseState.createEmptyBoardSnapshot(null);
    }

    const parsed = parseJsonSafely(notificationsJson, null);
    const frame = toNotificationFrame(parsed);
    return applyFrame(frame);
  };

  global.winuiBoardSseReducerGetCanonicalEnvelope = function winuiBoardSseReducerGetCanonicalEnvelope() {
    return JSON.stringify(toCanonicalEnvelope(state.snapshot));
  };
})(globalThis);
