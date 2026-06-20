// Platform-free PRODUCER driver, executed INSIDE the embedded V8 engine and by
// the Node oracle generator. It supports two storage backends:
//   1. Reference path: LocalStorageStorage.createLocalStorageBoardRuntimeBundle()
//      backed by browser localStorage (Node oracle)
//   2. Embedded-host path: real KV/Journal/Queue/Blob host objects supplied by
//      C# (Phase D)
//
// Both paths drive the same server-runtime producer to the same canonical
// published payload. The host-backed path is the real Phase D seam.

function createSyntheticRequest(method, url) {
  return {
    method: method,
    url: url,
    headers: {},
    on: function () {},
    [Symbol.asyncIterator]: async function* () {},
  };
}

function createSyntheticResponse() {
  var chunks = [];
  return {
    res: {
      writeHead: function () {},
      setHeader: function () {},
      write: function (chunk) {
        if (chunk != null) chunks.push(String(chunk));
      },
      end: function (chunk) {
        if (chunk != null) chunks.push(String(chunk));
      },
    },
    body: function () {
      return chunks.join('');
    },
  };
}

// Body-aware synthetic request: the runtime reads POST bodies via
// `for await (const chunk of req)`, so the async iterator must yield the body
// as a Uint8Array (no Buffer in the embedded V8 — readJsonBody falls back to
// TextDecoder over Uint8Array chunks).
function createSyntheticRequestWithBody(method, url, bodyText) {
  var body = bodyText == null ? '' : String(bodyText);
  return {
    method: method,
    url: url,
    headers: { 'content-type': 'application/json' },
    on: function () {},
    [Symbol.asyncIterator]: async function* () {
      if (body) {
        yield new TextEncoder().encode(body);
      }
    },
  };
}

// Status-capturing synthetic response: unlike createSyntheticResponse (used by
// the SSE bootstrap), this records the HTTP status code so the agentface proxy
// can faithfully relay the runtime's response to external callers.
function createSyntheticResponseWithStatus() {
  var chunks = [];
  var status = 200;
  function push(chunk) {
    if (chunk == null) return;
    chunks.push(typeof chunk === 'string' ? chunk : new TextDecoder().decode(chunk));
  }
  return {
    res: {
      writeHead: function (code) { if (typeof code === 'number') status = code; },
      setHeader: function () {},
      write: function (chunk) { push(chunk); },
      end: function (chunk) { push(chunk); },
    },
    status: function () { return status; },
    body: function () { return chunks.join(''); },
  };
}

function createParsedUrl(pathAndQuery) {
  var parts = String(pathAndQuery || '').split('?');
  var pathname = parts[0] || '/';
  var rawQuery = parts.length > 1 ? parts.slice(1).join('?') : '';
  var values = {};
  if (rawQuery) {
    rawQuery.split('&').forEach(function (pair) {
      if (!pair) return;
      var idx = pair.indexOf('=');
      var key = idx >= 0 ? pair.slice(0, idx) : pair;
      var value = idx >= 0 ? pair.slice(idx + 1) : '';
      values[decodeURIComponent(key)] = decodeURIComponent(value);
    });
  }
  return {
    pathname: pathname,
    searchParams: {
      has: function (key) {
        return Object.prototype.hasOwnProperty.call(values, key);
      },
      get: function (key) {
        return Object.prototype.hasOwnProperty.call(values, key) ? values[key] : null;
      },
    },
  };
}

async function bootstrapRuntime(runtime) {
  var parsedOneShotUrl = createParsedUrl('/api/board/sse?one-shot');
  for (var i = 0; i < 2; i += 1) {
    var req = createSyntheticRequest('GET', '/api/board/sse?one-shot');
    var syn = createSyntheticResponse();
    await runtime.handleRuntimeApi(req, syn.res, parsedOneShotUrl);
  }
  if (typeof runtime.__drainProcessAccumulatedLane === 'function') {
    await runtime.__drainProcessAccumulatedLane();
  }
}

function createRuntimeOptions(boardId, bundle, invocationAdapter, extras) {
  var refs = bundle.refs;
  if (!bundle.boardAdapter.callbackTransport) {
    bundle.boardAdapter.callbackTransport = createNoopCallbackTransport();
  }
  return Object.assign({
    boardId: boardId,
    boards: [{
      label: 'base',
      boardAdapter: bundle.boardAdapter,
      baseRef: refs.baseRef,
      boardRuntimeStoreRef: refs.boardRuntimeStoreRef,
      cardStoreRef: refs.cardStoreRef,
      outputsStoreRef: refs.outputsStoreRef,
      queueStoreRef: refs.queueStoreRef,
      artifactsStoreRef: refs.artifactsStoreRef,
      fetchedSourcesStoreRef: refs.fetchedSourcesStoreRef,
      chatStoreRef: refs.chatStoreRef,
      scratchStoreRef: refs.scratchStoreRef,
    }],
    invocationAdapter: invocationAdapter || { invoke: async function () { return { dispatched: false }; } },
    serverUrl: 'http://localhost:3000',
    logger: { info: function () {}, warn: function () {}, error: function () {} },
  }, extras || {});
}

function createNoopCallbackTransport() {
  return {
    createCallback: function (token) {
      return {
        token: token,
        via: {
          meta: 'board-live-cards',
          howToRun: 'built-in',
          whatToRun: {
            kind: 'built-in',
            value: 'board-live-cards',
          },
        },
      };
    },
  };
}

async function buildProducerPayload(boardId, cards, invocationAdapter) {
  const bundle = LocalStorageStorage.createLocalStorageBoardRuntimeBundle(boardId);
  const runtime = ServerRuntimeControlface.createSingleBoardServerRuntime(
    createRuntimeOptions(boardId, bundle, invocationAdapter)
  );

  const seed = await runtime.cardStore.set({ body: cards });
  if (!seed || seed.status !== 'success') {
    throw new Error('card seed failed: ' + ((seed && seed.error) || 'unknown'));
  }
  await bootstrapRuntime(runtime);
  return await runtime.buildPublishedRuntimePayload();
}

async function runProducerPayload(boardId, cardsJson) {
  const cards = JSON.parse(cardsJson);
  const payload = await buildProducerPayload(boardId, cards, null);
  return JSON.stringify(canonical(payload), null, 2) + '\n';
}

function createHostRefs(boardId) {
  var root = 'boards:' + boardId;
  return {
    baseRef: { kind: 'local-storage', value: root },
    boardRuntimeStoreRef: root + ':runtime-board',
    cardStoreRef: root + ':cards',
    outputsStoreRef: root + ':runtime-out',
    queueStoreRef: root + ':runtime',
    scratchStoreRef: root + ':scratch',
    chatStoreRef: root + ':chat',
    artifactsStoreRef: root + ':files',
    fetchedSourcesStoreRef: root + ':sources',
  };
}

function createDeterministicIdGenerator() {
  var counter = 0;
  return function (prefix) {
    counter += 1;
    return (prefix || 'id') + '-' + String(counter).padStart(6, '0');
  };
}

function createHostBridgeBundle(boardId) {
  if (!globalThis.HostStorageBridge) {
    throw new Error('HostStorageBridge is required for the embedded-host producer path');
  }

  var refs = createHostRefs(boardId);
  var nextId = createDeterministicIdGenerator();

  function parseJsonOrNull(raw) {
    return raw == null ? null : JSON.parse(raw);
  }

  function createKvStorage(scope) {
    return {
      read: async function (key) { return parseJsonOrNull(HostStorageBridge.KvRead(scope, key)); },
      write: async function (key, value) { HostStorageBridge.KvWrite(scope, key, JSON.stringify(value)); },
      delete: async function (key) { HostStorageBridge.KvDelete(scope, key); },
      listKeys: async function (prefix) { return JSON.parse(HostStorageBridge.KvListKeysJson(scope, prefix || null)); },
    };
  }

  function createBlobStorage(scope) {
    return {
      read: async function (key) { return HostStorageBridge.BlobRead(scope, key); },
      write: async function (key, content) { HostStorageBridge.BlobWrite(scope, key, String(content)); },
      exists: async function (key) { return !!HostStorageBridge.BlobExists(scope, key); },
      remove: async function (key) { HostStorageBridge.BlobRemove(scope, key); },
      listKeys: async function (prefix) { return JSON.parse(HostStorageBridge.BlobListKeysJson(scope, prefix || null)); },
      renameKey: async function (from, to) { return !!HostStorageBridge.BlobRenameKey(scope, from, to); },
      keyRef: async function (key) { return JSON.parse(HostStorageBridge.BlobKeyRefJson(scope, key)); },
    };
  }

  function createJournalStorage(scope) {
    return {
      append: async function (payload) { return JSON.parse(HostStorageBridge.JournalAppendJson(scope, JSON.stringify(payload))); },
      readAll: async function () { return JSON.parse(HostStorageBridge.JournalReadAllJson(scope)); },
      readAfter: async function (cursor) { return JSON.parse(HostStorageBridge.JournalReadAfterJson(scope, cursor || null)); },
      clear: async function () { HostStorageBridge.JournalClear(scope); },
    };
  }

  function createQueueStorage(scope) {
    return {
      enqueue: async function (body) {
        return JSON.parse(HostStorageBridge.QueueEnqueueJson(scope, JSON.stringify(body), null));
      },
      enqueueMany: async function (bodies) {
        var out = [];
        for (var i = 0; i < bodies.length; i += 1) out.push(await this.enqueue(bodies[i]));
        return out;
      },
      enqueueIfAbsent: async function (body, dedupKey) {
        var raw = HostStorageBridge.QueueEnqueueJson(scope, JSON.stringify(body), dedupKey);
        return raw === 'null' ? null : JSON.parse(raw);
      },
      lease: async function (opts) {
        return JSON.parse(HostStorageBridge.QueueLeaseJson(scope, JSON.stringify(opts || {})));
      },
      ack: async function (messageId, leaseToken) {
        return !!HostStorageBridge.QueueAck(scope, messageId, leaseToken);
      },
      nack: async function (messageId, leaseToken, opts) {
        var dead = !!(opts && opts.dead);
        var reason = opts && typeof opts.reason === 'string' ? opts.reason : null;
        return !!HostStorageBridge.QueueNack(scope, messageId, leaseToken, dead, reason);
      },
      peekActive: async function (prefix) {
        return JSON.parse(HostStorageBridge.QueuePeekActiveJson(scope, prefix || null));
      },
      peekDeadLetter: async function (prefix) {
        return JSON.parse(HostStorageBridge.QueuePeekDeadLetterJson(scope, prefix || null));
      },
      stage: async function (body, opts) {
        var dedupKey = opts && typeof opts.dedupKey === 'string' ? opts.dedupKey : null;
        var raw = HostStorageBridge.QueueStageJson(scope, JSON.stringify(body), dedupKey);
        return raw === 'null' ? null : JSON.parse(raw);
      },
      commitStaged: async function (messageId) {
        return !!HostStorageBridge.QueueCommitStaged(scope, messageId);
      },
      discardStaged: async function (messageId, reason) {
        return !!HostStorageBridge.QueueDiscardStaged(scope, messageId, reason || null);
      },
      peekStaged: async function (prefix) {
        return JSON.parse(HostStorageBridge.QueuePeekStagedJson(scope, prefix || null));
      },
    };
  }

  function createChatStorage(scope) {
    var kv = createKvStorage(scope);
    function safeCardKey(cardId) {
      return String(cardId).replace(/[^a-zA-Z0-9_-]/g, '_');
    }
    function journal(cardId) {
      return createJournalStorage(scope + ':journal:' + safeCardKey(cardId));
    }
    function toRecord(entry) {
      var payload = entry && entry.payload && typeof entry.payload === 'object' ? entry.payload : {};
      return {
        id: entry.id,
        role: typeof payload.role === 'string' ? payload.role : 'system',
        text: typeof payload.text === 'string' ? payload.text : '',
        files: Array.isArray(payload.files) ? payload.files : [],
        turn: typeof payload.turn === 'string' ? payload.turn : '',
        updated_at: typeof payload.updated_at === 'string' ? payload.updated_at : '',
      };
    }
    function processingKey(cardId) {
      return 'chats/' + safeCardKey(cardId) + '/processing';
    }
    function configKey(cardId) {
      return 'chats/' + safeCardKey(cardId) + '/config';
    }
    return {
      append: async function (cardId, role, text, files, turn) {
        return (await journal(cardId).append({
          role: role,
          text: text,
          files: files || [],
          turn: turn || '',
          updated_at: '1970-01-01T00:00:00.0000000+00:00',
        })).id;
      },
      readAll: async function (cardId) {
        return (await journal(cardId).readAll()).map(toRecord);
      },
      readAfter: async function (cardId, cursor) {
        var result = await journal(cardId).readAfter(cursor);
        return {
          records: result.entries.map(toRecord),
          cursor: result.newCursor,
        };
      },
      clear: async function (cardId) { await journal(cardId).clear(); },
      setProcessing: async function (cardId, active) {
        if (active) await kv.write(processingKey(cardId), true);
        else await kv.delete(processingKey(cardId));
      },
      isProcessing: async function (cardId) { return (await kv.read(processingKey(cardId))) === true; },
      getConfig: async function (cardId) { return (await kv.read(configKey(cardId))) || {}; },
      setConfig: async function (cardId, patch) {
        var existing = (await kv.read(configKey(cardId))) || {};
        await kv.write(configKey(cardId), Object.assign({}, existing, patch));
      },
    };
  }

  function createScratchStorage(scope) {
    var blob = createBlobStorage(scope);
    return Object.assign({}, blob, {
      getUniqueKey: function (prefix, suffix) {
        return [prefix || 'scratch', nextId('scratch'), suffix || 'tmp'].join('-');
      },
        create: async function (data, prefix, suffix) {
        var key = this.getUniqueKey(prefix, suffix);
          await blob.write(key, data);
        return key;
      },
        keyRef: async function (key) { return blob.keyRef(key); },
      config: {
          get: async function (key) {
          return parseJsonOrNull(HostStorageBridge.MetaGet('scratch', scope, key));
        },
          set: async function (key, value) {
          HostStorageBridge.MetaSet('scratch', scope, key, JSON.stringify(value));
        },
      },
    });
  }

  function createArchiveFactory(scope) {
    return {
      stream: function (name) { return createJournalStorage(scope + ':stream:' + name); },
      blob: function (name) { return createBlobStorage(scope + ':blob:' + name); },
      listStreams: async function () { return []; },
      listBlobs: async function () { return []; },
      config: {
        get: async function (key) {
          return parseJsonOrNull(HostStorageBridge.MetaGet('archive', scope, key));
        },
        set: async function (key, value) {
          HostStorageBridge.MetaSet('archive', scope, key, JSON.stringify(value));
        },
      },
    };
  }

  function createRelayLock() {
    var held = false;
    return {
      tryAcquire: function () {
        if (held) return null;
        held = true;
        return function () { held = false; };
      },
    };
  }

  return {
    refs: refs,
    boardAdapter: {
      kvStorage: function (namespace) { return createKvStorage(refs.baseRef.value + ':' + (namespace || 'root')); },
      kvStorageForRef: function (ref) { return createKvStorage(ref); },
      blobStorage: function (namespace) { return createBlobStorage(refs.baseRef.value + ':' + (namespace || 'root')); },
      blobStorageForRef: function (ref) { return createBlobStorage(ref); },
      chatStorageForRef: function (ref) { return createChatStorage(ref); },
      queueStorageForRef: function (ref, lane) { return createQueueStorage(ref + ':queue:' + lane); },
      scratchStorage: function () { return createScratchStorage(refs.scratchStoreRef); },
      scratchStorageForRef: function (ref) { return createScratchStorage(ref); },
      archiveFactory: function () { return createArchiveFactory(refs.baseRef.value + ':archive'); },
      archiveFactoryForRef: function (ref) { return createArchiveFactory(ref); },
      journalStorage: function () { return createJournalStorage(refs.baseRef.value + ':journal'); },
      journalStorageForRef: function (ref) { return createJournalStorage(ref + ':journal'); },
      lock: createRelayLock(),
      callbackTransport: createNoopCallbackTransport(),
      dispatchExecution: async function () {
        return { dispatched: false, error: 'dispatchExecution is not configured for this embedded harness' };
      },
      resolveBlob: async function (ref) {
        var raw = HostStorageBridge.ResolveBlobRef(ref.kind, ref.value);
        if (raw == null) throw new Error('blob not found for ref: ' + JSON.stringify(ref));
        return raw;
      },
      hashFn: function (value) { return JSON.stringify(canonical(value)); },
      genId: function () { return nextId('gen'); },
      requestProcessAccumulated: function () {},
      publishBoardChangeNotifications: function () {},
      warn: function () {},
    },
  };
}

function createHostInvocationAdapter() {
  if (!globalThis.HostInvocationBridge) {
    return {
      invoke: async function () { return { dispatched: false, error: 'HostInvocationBridge missing' }; },
      describe: async function () { return null; },
    };
  }

  return {
    invoke: async function (ref, args) {
      return JSON.parse(HostInvocationBridge.Invoke(JSON.stringify(ref), JSON.stringify(args)));
    },
    describe: async function (ref) {
      var raw = HostInvocationBridge.Describe(JSON.stringify(ref));
      return raw ? JSON.parse(raw) : null;
    },
  };
}

async function buildHostBackedProducerPayload(boardId, cards) {
  const bundle = createHostBridgeBundle(boardId);
  const runtime = ServerRuntimeControlface.createSingleBoardServerRuntime(
    createRuntimeOptions(boardId, bundle, createHostInvocationAdapter())
  );

  const seed = await runtime.cardStore.set({ body: cards });
  if (!seed || seed.status !== 'success') {
    throw new Error('host-backed card seed failed: ' + ((seed && seed.error) || 'unknown'));
  }
  await bootstrapRuntime(runtime);
  return await runtime.buildPublishedRuntimePayload();
}

async function runHostBackedProducerPayload(boardId, cardsJson) {
  const cards = JSON.parse(cardsJson);
  const payload = await buildHostBackedProducerPayload(boardId, cards);
  return JSON.stringify(canonical(payload), null, 2) + '\n';
}

async function runInvocationAdapterProof() {
  if (!globalThis.HostInvocationBridge) {
    throw new Error('HostInvocationBridge is required for invocation proof');
  }

  HostInvocationBridge.Reset();
  const boardId = 'phase-d-invoke';
  const bundle = createHostBridgeBundle(boardId);
  const invocationAdapter = createHostInvocationAdapter();
  const runtime = ServerRuntimeControlface.createSingleBoardServerRuntime(
    createRuntimeOptions(boardId, bundle, invocationAdapter)
  );

  const ref = {
    meta: 'chat-handler',
    howToRun: 'host-llm',
    whatToRun: { kind: 'copilot', value: 'phase-d-hosted-llm' },
  };
  const describe = await invocationAdapter.describe(ref);
  await runtime.handleChatAgentRequest({
    boardId: boardId,
    ref: ref,
    args: { prompt: 'hello from phase d', lane: 'copilot' },
  });

  return JSON.stringify(canonical({
    describe: describe,
    lastInvocation: JSON.parse(HostInvocationBridge.GetLastInvocationJson() || 'null'),
  }), null, 2) + '\n';
}

// ---------------------------------------------------------------------------
// Long-lived WinUI runtime: one host-backed runtime kept alive for the app so
// the shell can mutate the board and re-read the published snapshot without
// rebuilding the whole brain each time. Board-change notifications collapse to
// an in-process call into the C# HostNotifier (the embedded boundary model:
// watchers/SSE become direct in-process calls).
// ---------------------------------------------------------------------------

async function initWinuiRuntime(boardId, cardsJson) {
  const cards = JSON.parse(cardsJson);
  const bundle = createHostBridgeBundle(boardId);

  if (globalThis.HostNotifier) {
    bundle.boardAdapter.publishBoardChangeNotifications = function () {
      try { HostNotifier.NotifyBoardChanged(); } catch (e) {}
    };
  }

  const runtime = ServerRuntimeControlface.createSingleBoardServerRuntime(
    createRuntimeOptions(boardId, bundle, createHostInvocationAdapter())
  );

  const seed = await runtime.cardStore.set({ body: cards });
  if (!seed || seed.status !== 'success') {
    throw new Error('winui runtime seed failed: ' + ((seed && seed.error) || 'unknown'));
  }
  await bootstrapRuntime(runtime);

  globalThis.__winuiRuntime = runtime;
  globalThis.__winuiBoardId = boardId;
  return JSON.stringify(canonical(await runtime.buildPublishedRuntimePayload()), null, 2) + '\n';
}

function getWinuiRuntime() {
  var runtime = globalThis.__winuiRuntime;
  if (!runtime) throw new Error('winui runtime not initialized');
  return runtime;
}

async function winuiBuildSnapshot() {
  const runtime = getWinuiRuntime();
  return JSON.stringify(canonical(await runtime.buildPublishedRuntimePayload()), null, 2) + '\n';
}

async function winuiAddCard(cardJson) {
  const runtime = getWinuiRuntime();
  const card = JSON.parse(cardJson);

  const current = await runtime.buildPublishedRuntimePayload();
  const existing = Array.isArray(current.cardDefinitions) ? current.cardDefinitions : [];
  const merged = existing.concat([card]);

  const seed = await runtime.cardStore.set({ body: merged });
  if (!seed || seed.status !== 'success') {
    throw new Error('winui add card failed: ' + ((seed && seed.error) || 'unknown'));
  }
  await bootstrapRuntime(runtime);

  return JSON.stringify(canonical(await runtime.buildPublishedRuntimePayload()), null, 2) + '\n';
}

// Real agentface proxy: forward an HTTP request straight into the long-lived
// runtime's handleRuntimeApi. This makes the embedded agentface a faithful
// face over the SAME runtime API the Node server exposes (MCP /api/board/mcp,
// /api/board/mcp-raw, SSE one-shot, card files, …) — no stubbed semantics.
async function winuiHandleRuntimeApi(method, pathAndQuery, bodyJson) {
  const runtime = getWinuiRuntime();
  const req = createSyntheticRequestWithBody(method || 'GET', pathAndQuery, bodyJson);
  const syn = createSyntheticResponseWithStatus();
  const parsed = createParsedUrl(pathAndQuery);
  const handled = await runtime.handleRuntimeApi(req, syn.res, parsed);
  if (!handled) {
    return JSON.stringify({
      status: 404,
      body: JSON.stringify({ error: 'runtime did not handle ' + method + ' ' + pathAndQuery }),
    });
  }
  return JSON.stringify({ status: syn.status(), body: syn.body() });
}

