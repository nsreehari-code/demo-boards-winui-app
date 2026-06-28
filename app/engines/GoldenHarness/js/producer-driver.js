// Platform-free PRODUCER driver, executed INSIDE the embedded V8 engine and by
// the Node oracle generator. It supports two storage backends:
//   1. Reference path: LocalStorageStorage.createLocalStorageBoardRuntimeBundle()
//      backed by browser localStorage (Node oracle)
//   2. Embedded-host path: real KV/Journal/Queue/Blob host objects supplied by
//      C# (Phase D)
//
// Both paths drive the same server-runtime producer to the same canonical
// published payload. The host-backed path is the real Phase D seam.

function createSyntheticRequest(method, url, bodyText, headers) {
  var encoder = new TextEncoder();
  var body = bodyText == null ? '' : String(bodyText);
  var chunks = body === '' ? [] : [encoder.encode(body)];
  return {
    method: method,
    url: url,
    headers: headers || {},
    on: function (event, handler) {
      if (typeof handler !== 'function') return;
      if (event === 'data') {
        chunks.forEach(function (chunk) { handler(chunk); });
      }
      if (event === 'end') {
        handler();
      }
    },
    [Symbol.asyncIterator]: async function* () {
      for (var i = 0; i < chunks.length; i += 1) {
        yield chunks[i];
      }
    },
  };
}

function createSyntheticResponse() {
  var decoder = new TextDecoder();
  var chunks = [];
  var statusCode = 200;
  var headers = {};
  function chunkToText(chunk) {
    if (chunk == null) return '';
    if (typeof chunk === 'string') return chunk;
    if (chunk instanceof Uint8Array) {
      return decoder.decode(chunk);
    }
    if (ArrayBuffer.isView(chunk)) {
      return decoder.decode(new Uint8Array(chunk.buffer, chunk.byteOffset, chunk.byteLength));
    }
    if (chunk instanceof ArrayBuffer) {
      return decoder.decode(new Uint8Array(chunk));
    }
    return String(chunk);
  }
  return {
    res: {
      writeHead: function (status, nextHeaders) {
        statusCode = Number(status) || 200;
        if (nextHeaders && typeof nextHeaders === 'object') {
          Object.assign(headers, nextHeaders);
        }
      },
      setHeader: function (name, value) {
        headers[String(name || '').toLowerCase()] = value;
      },
      write: function (chunk) {
        if (chunk != null) chunks.push(chunkToText(chunk));
      },
      end: function (chunk) {
        if (chunk != null) chunks.push(chunkToText(chunk));
      },
    },
    body: function () {
      return chunks.join('');
    },
    statusCode: function () {
      return statusCode;
    },
    headers: function () {
      return headers;
    },
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

function getHostControlfaceBridge() {
  if (!globalThis.HostControlfaceBridge) {
    throw new Error('HostControlfaceBridge is required for embedded controlface routes');
  }
  return globalThis.HostControlfaceBridge;
}

function normalizeServerUrl(value) {
  return typeof value === 'string' ? value.trim().replace(/\/+$/, '') : '';
}

function resolveHostServerUrl() {
  if (!globalThis.HostInvocationBridge || typeof HostInvocationBridge.GetServerUrl !== 'function') {
    return '';
  }
  try {
    return normalizeServerUrl(HostInvocationBridge.GetServerUrl());
  } catch {
    return '';
  }
}

function createHttpCallbackTransport(baseUrl) {
  return {
    createCallback: function (token) {
      return {
        token: token,
        via: {
          meta: 'board-live-cards',
          howToRun: 'http:post',
          whatToRun: {
            kind: 'http-url',
            value: baseUrl,
          },
        },
      };
    },
  };
}

function createHostCallbackTransport(boardId) {
  var serverUrl = resolveHostServerUrl();
  if (!serverUrl) {
    return createNoopCallbackTransport();
  }
  var webhooksUrl = serverUrl + '/api/boards/' + encodeURIComponent(boardId) + '/mcp-webhooks';
  return createHttpCallbackTransport(webhooksUrl);
}

function createManagedBoardsApi() {
  var shared = globalThis.ControlfaceEmbeddedShared;
  if (!shared
    || typeof shared.createManagedBoardsApi !== 'function'
    || typeof shared.createManagedBoardLifecycle !== 'function') {
    throw new Error('ControlfaceEmbeddedShared managed-board shared helpers are required');
  }
  var bridge = getHostControlfaceBridge();
  var lifecycle = shared.createManagedBoardLifecycle({
    storage: {
      list: function () {
        return JSON.parse(bridge.ListBoardContainerRecordsJson() || '[]').map(function (record) {
          var recordId = typeof (record && record.id) === 'string' ? record.id.trim() : '';
          return { id: recordId, record: record };
        });
      },
      get: function (boardId) {
        var raw = bridge.GetBoardContainerRecordJson(String(boardId || ''));
        return raw ? JSON.parse(raw) : null;
      },
      has: function (boardId) {
        return !!bridge.HasBoardContainerRecord(String(boardId || ''));
      },
      put: function (boardId, record) {
        bridge.PutBoardContainerRecordJson(String(boardId || ''), JSON.stringify(record));
      },
      set: function (boardId, record) {
        bridge.SetBoardContainerRecordJson(String(boardId || ''), JSON.stringify(record));
      },
      getLayout: function (boardId) {
        var raw = bridge.GetBoardContainerLayoutJson(String(boardId || ''));
        return raw ? JSON.parse(raw) : null;
      },
      setLayout: function (boardId, layout) {
        bridge.SetBoardContainerLayoutJson(String(boardId || ''), JSON.stringify(layout));
      },
      removeLayout: function (boardId) {
        bridge.RemoveBoardContainerLayout(String(boardId || ''));
      },
      archive: function (boardId) {
        var raw = bridge.ArchiveBoardContainerJson(String(boardId || ''));
        return raw ? JSON.parse(raw) : null;
      },
    },
  });
  return shared.createManagedBoardsApi({
    lifecycle: lifecycle,
    hostBridge: bridge,
    getActiveBoardId: function () {
      return globalThis.__winuiBoardId || '';
    },
    invokeRuntimeRoute: function (method, path, bodyText) {
      return winuiHandleRuntimeApi(method, path, bodyText, '{}');
    },
    resolveBoardConfig: function (boardId, record) {
      if (typeof bridge.ResolveBoardConfig !== 'function') {
        return null;
      }
      var raw = bridge.ResolveBoardConfig(String(boardId || ''), JSON.stringify(record || {}));
      var parsed = raw && String(raw).trim() ? JSON.parse(raw) : null;
      if (parsed && typeof parsed === 'object' && parsed.resolvedBoardConfig && typeof parsed.resolvedBoardConfig === 'object') {
        return parsed.resolvedBoardConfig;
      }
      return parsed;
    },
    ensureWorkspace: function (board) {
      if (typeof bridge.SetupBoardWorkspace !== 'function') {
        return;
      }
      try {
        bridge.SetupBoardWorkspace(String((board && board.id) || ''), JSON.stringify(board || {}));
      } catch (e) {
        // Workspace setup is best-effort in the embedded host; admin-card seeding (the parity
        // contract) must still proceed even if the AI workspace cannot be provisioned here.
      }
    },
    provisionRuntime: function (board) {
      return ensureWinuiRuntime(String((board && board.id) || ''));
    },
    invokeRuntimeTool: function (runtimeEntry, boardId, routeKind, payload) {
      return invokeWinuiRuntimeTool(runtimeEntry, boardId, routeKind, payload);
    },
  });
}

function createMcpExtrasApi() {
  var shared = globalThis.ControlfaceEmbeddedShared;
  if (!shared || typeof shared.createSampleTemplateCatalogApi !== 'function') {
    throw new Error('ControlfaceEmbeddedShared.createSampleTemplateCatalogApi is required');
  }
  var bridge = getHostControlfaceBridge();
  return shared.createSampleTemplateCatalogApi({
    listEntries: function () {
      return { entries: JSON.parse(bridge.ListSampleTemplateEntriesJson() || '[]') };
    },
    getEnvelope: function (key) {
      return JSON.parse(bridge.GetSampleTemplateEnvelopeJson(String(key || '')));
    },
  });
}

function createAgentfaceMcpApi() {
  if (globalThis.__winuiAgentfaceMcpApi) {
    return globalThis.__winuiAgentfaceMcpApi;
  }
  var shared = globalThis.AgentfaceEmbeddedShared;
  if (!shared
    || typeof shared.createEmbeddedAgentfaceMcp !== 'function'
    || typeof shared.createHttpRouteAgentfaceSurface !== 'function') {
    throw new Error('AgentfaceEmbeddedShared shared surface helpers are required');
  }
  var bridge = getHostControlfaceBridge();
  var surface = shared.createHttpRouteAgentfaceSurface({
    manifest: JSON.parse(bridge.GetAgentfaceToolsManifestJson()),
    invokeHttpRoute: function (method, path, bodyText) {
      return winuiHandleRuntimeApi(method, path, bodyText, '{}');
    },
  });
  globalThis.__winuiAgentfaceMcpApi = shared.createEmbeddedAgentfaceMcp({
    surface: surface,
  });
  return globalThis.__winuiAgentfaceMcpApi;
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
    bundle.boardAdapter.callbackTransport = createHostCallbackTransport(boardId);
  }
  var boardConfig = {
    label: 'base',
    boardAdapter: bundle.boardAdapter,
    nonCoreAdapter: bundle.nonCoreAdapter || bundle.boardAdapter,
    baseRef: refs.baseRef,
    boardRuntimeStoreRef: refs.boardRuntimeStoreRef,
    cardStoreRef: refs.cardStoreRef,
    outputsStoreRef: refs.outputsStoreRef,
    queueStoreRef: refs.queueStoreRef,
    artifactsStoreRef: refs.artifactsStoreRef,
    fetchedSourcesStoreRef: refs.fetchedSourcesStoreRef,
    chatStoreRef: refs.chatStoreRef,
    scratchStoreRef: refs.scratchStoreRef,
    taskExecutorRef: createHostTaskExecutorRef(boardId),
    chatHandlerFlow: createHostedChatHandlerFlow(),
  };
  return Object.assign({
    boardId: boardId,
    apiBasePath: '/api/boards/' + encodeURIComponent(boardId),
    boards: [boardConfig],
    invocationAdapter: invocationAdapter || { invoke: async function () { return { dispatched: false }; } },
    chatFlowRunner: createHostChatFlowRunner(),
    serverUrl: resolveHostServerUrl() || undefined,
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

  // Shared filesystem-backed blob storage (kind: 'fs-path'). Used for the
  // fetched-sources store so the out-of-process node board-worker (which only
  // resolves `fs-path` refs) and the embedded host read/write the SAME file.
  function createSharedFsBlobStorage(scope) {
    return {
      read: async function (key) { return HostStorageBridge.SharedBlobRead(scope, key); },
      write: async function (key, content) { HostStorageBridge.SharedBlobWrite(scope, key, String(content)); },
      exists: async function (key) { return !!HostStorageBridge.SharedBlobExists(scope, key); },
      remove: async function (key) { HostStorageBridge.SharedBlobRemove(scope, key); },
      listKeys: async function (prefix) { return JSON.parse(HostStorageBridge.SharedBlobListKeysJson(scope, prefix || null)); },
      renameKey: async function (from, to) { return !!HostStorageBridge.SharedBlobRenameKey(scope, from, to); },
      keyRef: async function (key) { return JSON.parse(HostStorageBridge.SharedBlobKeyRefJson(scope, key)); },
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

  var boardAdapter = {
      kvStorage: function (namespace) { return createKvStorage(refs.baseRef.value + ':' + (namespace || 'root')); },
      kvStorageForRef: function (ref) { return createKvStorage(ref); },
      blobStorage: function (namespace) { return createBlobStorage(refs.baseRef.value + ':' + (namespace || 'root')); },
      blobStorageForRef: function (ref) {
        // The fetched-sources store must be shared on-disk (fs-path) so the
        // out-of-process node board-worker and the embedded host see the same
        // staged source files. All other stores stay embedded-host-blob.
        if (ref === refs.fetchedSourcesStoreRef) { return createSharedFsBlobStorage(ref); }
        return createBlobStorage(ref);
      },
      chatStorageForRef: function (ref) { return createChatStorage(ref); },
      queueStorageForRef: function (ref, lane) { return createQueueStorage(ref + ':queue:' + lane); },
      scratchStorage: function () { return createScratchStorage(refs.scratchStoreRef); },
      scratchStorageForRef: function (ref) { return createScratchStorage(ref); },
      archiveFactory: function () { return createArchiveFactory(refs.baseRef.value + ':archive'); },
      archiveFactoryForRef: function (ref) { return createArchiveFactory(ref); },
      journalStorage: function () { return createJournalStorage(refs.baseRef.value + ':journal'); },
      journalStorageForRef: function (ref) { return createJournalStorage(ref + ':journal'); },
      lock: createRelayLock(),
      callbackTransport: createHostCallbackTransport(boardId),
      dispatchExecution: async function (ref, args) {
        if (!globalThis.HostInvocationBridge || typeof HostInvocationBridge.InvokeDispatch !== 'function') {
          throw new Error('HostInvocationBridge.InvokeDispatch is required for embedded executor dispatch');
        }
        // Fire-and-forget: the board-worker runs out-of-process and reports back via
        // the /mcp-webhooks callback, which must be served by this same single-threaded
        // runtime. A synchronous spawn would block that callback and deadlock, so the
        // host launches the worker on a background thread and returns immediately.
        var result = JSON.parse(HostInvocationBridge.InvokeDispatch(JSON.stringify(ref), JSON.stringify(args || {})));
        if (!result || result.dispatched !== true) {
          throw new Error(result && result.error ? String(result.error) : 'embedded executor dispatch failed');
        }
        return result;
      },
      resolveBlob: async function (ref) {
        var raw = HostStorageBridge.ResolveBlobRef(ref.kind, ref.value);
        if (raw == null) throw new Error('blob not found for ref: ' + JSON.stringify(ref));
        return raw;
      },
      supportsDirectSourceOutput: function (ref) {
        // The embedded host runs the board-worker out-of-process and writes
        // fetched source output directly to the shared fs-path staged blob.
        var howToRun = ref && ref.howToRun;
        return howToRun === 'embedded-host'
          || howToRun === 'queue-storage'
          || howToRun === 'http:post'
          || howToRun === 'in-process-loop';
      },
      hashFn: function (value) { return JSON.stringify(canonical(value)); },
      genId: function () { return nextId('gen'); },
      requestProcessAccumulated: function () {},
      publishBoardChangeNotifications: function () {},
      warn: function () {},
  };

  // Non-core platform adapter: the board adapter plus the executor + schema +
  // absolute-blob capabilities required by the MCP preflight/discovery tools
  // (validateCardPreflight, admin-upsert-card, inspect/probe/simulate, …).
  // Mirrors createFsBoardNonCorePlatformAdapter from yaml-flow's localfs host.
  var nonCoreAdapter = Object.assign({}, boardAdapter, {
    validateSchema: function (card) {
      var bridge = getHostControlfaceBridge();
      if (typeof bridge.ValidateCardSchema !== 'function') {
        return { ok: true, errors: [] };
      }
      var raw = bridge.ValidateCardSchema(JSON.stringify(card || {}));
      var parsed = raw && String(raw).trim() ? JSON.parse(raw) : null;
      return {
        ok: !!(parsed && parsed.ok),
        errors: parsed && Array.isArray(parsed.errors) ? parsed.errors : [],
      };
    },
    invokeExecutor: async function (ref, subcommand, opts) {
      if (!globalThis.HostInvocationBridge || typeof HostInvocationBridge.InvokeExecutor !== 'function') {
        throw new Error('HostInvocationBridge.InvokeExecutor is required for non-core executor invocation');
      }
      var input = opts && typeof opts.input === 'string' ? opts.input : '';
      return HostInvocationBridge.InvokeExecutor(JSON.stringify(ref || {}), String(subcommand || ''), input);
    },
    absoluteBlob: createBlobStorage(refs.baseRef.value + ':absolute'),
    executorTimeouts: { validationMs: 10000, preflightMs: 60000, probeMs: 60000, describeMs: 10000 },
  });

  return {
    refs: refs,
    boardAdapter: boardAdapter,
    nonCoreAdapter: nonCoreAdapter,
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

function createHostTaskExecutorRef(boardId) {
  return {
    meta: 'task-executor',
    howToRun: 'embedded-host',
    whatToRun: {
      kind: 'embedded-host',
      value: 'board-worker',
    },
    extra: { boardId: boardId },
  };
}

function createHostedChatHandlerFlow() {
  return { kind: 'hosted-chat-agent' };
}

function createHostChatFlowRunner() {
  if (!globalThis.HostInvocationBridge) {
    return {
      run: async function () {
        return { dispatched: false, error: 'HostInvocationBridge missing' };
      },
    };
  }

  return {
    run: async function (handlerFlow, args) {
      var ref = {
        meta: 'chat-handler-flow',
        howToRun: 'embedded-host',
        whatToRun: handlerFlow && typeof handlerFlow === 'object'
          ? handlerFlow
          : { kind: 'hosted-chat-agent' },
      };
      // Fire-and-forget: the chat agent runs out-of-process and delivers its reply,
      // chat-processing state, and notifications via HTTP calls back to this same
      // single-threaded runtime. A synchronous spawn would block those callbacks and
      // deadlock, so dispatch on a background thread and return immediately.
      if (typeof HostInvocationBridge.InvokeDispatch === 'function') {
        return JSON.parse(HostInvocationBridge.InvokeDispatch(JSON.stringify(ref), JSON.stringify(args || {})));
      }
      return JSON.parse(HostInvocationBridge.Invoke(JSON.stringify(ref), JSON.stringify(args || {})));
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
  const runtime = await buildWinuiRuntime(boardId, cards);

  globalThis.__winuiRuntime = runtime;
  globalThis.__winuiBoardId = boardId;

  await winuiEnsureBoardSeeded(boardId);

  return JSON.stringify(canonical(await runtime.buildPublishedRuntimePayload()), null, 2) + '\n';
}

// Warm-start bootstrap parity with the hosted controlface server: the initial board's
// managed record is ensured and its admin template cards are seeded into the active runtime
// via the SAME converged manage-boards path used by add-board. This guarantees control-plane
// admin cards (e.g. gandalf-intake) exist even when the board record already persists from a
// previous session and the test harness therefore skips add-board.
async function winuiEnsureBoardSeeded(boardId) {
  var normalizedBoardId = String(boardId || '');
  if (!normalizedBoardId) {
    return;
  }
  var shared = globalThis.ControlfaceEmbeddedShared;
  if (!shared || typeof shared.createManagedBoardsApi !== 'function') {
    return;
  }

  var bridge = getHostControlfaceBridge();
  var defaultRecord = {
    id: normalizedBoardId,
    label: normalizedBoardId,
    ai: 'copilot',
    aiWorkspaceTemplate: 'default',
    refsTemplate: 'localfs-default',
    uiTemplate: 'default',
  };

  var existingRecord = null;
  try {
    var existingRaw = bridge.GetBoardContainerRecordJson(normalizedBoardId);
    existingRecord = existingRaw && String(existingRaw).trim() ? JSON.parse(existingRaw) : null;
  } catch (e) {
    existingRecord = null;
  }

  var record = Object.assign({}, defaultRecord, existingRecord || {});
  record.id = normalizedBoardId;

  var hasRecord = false;
  try {
    hasRecord = !!bridge.HasBoardContainerRecord(normalizedBoardId);
  } catch (e) {
    hasRecord = false;
  }

  var api = createManagedBoardsApi();
  var requestBody = JSON.stringify({
    subcommand: hasRecord ? 'save-board-record' : 'add-board',
    args: { boardId: normalizedBoardId, record: record },
  });
  var seedResponse = await api.handleRequest('POST', requestBody);
  var seedEnvelope = seedResponse && String(seedResponse).trim() ? JSON.parse(seedResponse) : null;
  var seedStatusCode = seedEnvelope && Number(seedEnvelope.statusCode) ? Number(seedEnvelope.statusCode) : 0;
  if (seedStatusCode >= 400 || seedStatusCode === 0) {
    var seedBodyText = seedEnvelope && typeof seedEnvelope.body === 'string' ? seedEnvelope.body : '';
    throw new Error('winuiEnsureBoardSeeded failed (status ' + seedStatusCode + '): ' + seedBodyText);
  }
}

// Per-board runtime registry. The embedded host keeps one host-backed runtime alive per
// managed board so manage-boards can provision and seed boards other than the active one
// (mirrors the hosted controlface boardRuntimes map). The active board is additionally
// tracked via __winuiBoardId/__winuiRuntime for the shell snapshot/add-card fast path.
function getWinuiRuntimeRegistry() {
  if (!globalThis.__winuiRuntimes || typeof globalThis.__winuiRuntimes.get !== 'function') {
    globalThis.__winuiRuntimes = new Map();
  }
  return globalThis.__winuiRuntimes;
}

async function buildWinuiRuntime(boardId, cards) {
  const normalizedBoardId = String(boardId || '');
  const bundle = createHostBridgeBundle(normalizedBoardId);

  if (globalThis.HostNotifier) {
    bundle.boardAdapter.publishBoardChangeNotifications = function (notifications) {
      try {
        HostNotifier.NotifyBoardNotifications(JSON.stringify(Array.isArray(notifications) ? notifications : []));
      } catch (e) {}
      try { HostNotifier.NotifyBoardChanged(); } catch (e) {}
    };
  }

  const runtime = ServerRuntimeControlface.createSingleBoardServerRuntime(
    createRuntimeOptions(normalizedBoardId, bundle, createHostInvocationAdapter())
  );

  const seed = await runtime.cardStore.set({ body: Array.isArray(cards) ? cards : [] });
  if (!seed || seed.status !== 'success') {
    throw new Error('winui runtime seed failed: ' + ((seed && seed.error) || 'unknown'));
  }
  await bootstrapRuntime(runtime);

  getWinuiRuntimeRegistry().set(normalizedBoardId, runtime);
  return runtime;
}

// provisionRuntime adapter for the converged manage-boards seeding path. Returns the
// existing runtime for the board when one is already alive (e.g. the active warm runtime),
// otherwise builds and registers a fresh host-backed runtime for that board.
async function ensureWinuiRuntime(boardId) {
  const normalizedBoardId = String(boardId || '');
  const registry = getWinuiRuntimeRegistry();
  if (registry.has(normalizedBoardId)) {
    return registry.get(normalizedBoardId);
  }
  if (normalizedBoardId && normalizedBoardId === globalThis.__winuiBoardId && globalThis.__winuiRuntime) {
    registry.set(normalizedBoardId, globalThis.__winuiRuntime);
    return globalThis.__winuiRuntime;
  }
  return await buildWinuiRuntime(normalizedBoardId, []);
}

// invokeRuntimeTool adapter: routes a control-plane/runtime tool call directly into a
// specific board's runtime entry (bypassing the active-board HTTP router so non-active
// boards can be seeded during provisioning).
async function invokeWinuiRuntimeTool(runtime, boardId, routeKind, payload) {
  const normalizedBoardId = String(boardId || '');
  const path = '/api/boards/' + encodeURIComponent(normalizedBoardId) + '/' + routeKind;
  const parsedUrl = createParsedUrl(path);
  const request = createSyntheticRequest(
    'POST',
    path,
    JSON.stringify(payload || {}),
    { 'content-type': 'application/json' }
  );
  const response = createSyntheticResponse();

  await runtime.handleRuntimeApi(request, response.res, parsedUrl);
  if (typeof runtime.__drainProcessAccumulatedLane === 'function') {
    await runtime.__drainProcessAccumulatedLane();
  }

  const statusCode = response.statusCode();
  const bodyText = response.body();
  const parsed = bodyText && String(bodyText).trim() ? JSON.parse(bodyText) : null;
  if (statusCode >= 400) {
    const message = parsed && typeof parsed === 'object' && !Array.isArray(parsed) && typeof parsed.error === 'string' && parsed.error.trim()
      ? parsed.error.trim()
      : 'runtime request failed';
    const error = new Error(message);
    error.statusCode = statusCode;
    throw error;
  }
  return parsed;
}

function getWinuiRuntime(boardId) {
  var registry = getWinuiRuntimeRegistry();
  var targetId = boardId ? String(boardId) : (globalThis.__winuiBoardId || '');
  var runtime = targetId && registry.has(targetId) ? registry.get(targetId) : null;
  if (!runtime) {
    runtime = globalThis.__winuiRuntime;
  }
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

async function winuiHandleRuntimeApi(method, path, bodyText, requestHeadersJson) {
  const activeBoardId = globalThis.__winuiBoardId || 'winui-board';
  const managedBoardsApi = createManagedBoardsApi();
  const mcpExtrasApi = createMcpExtrasApi();
  const agentfaceMcpApi = createAgentfaceMcpApi();
  const requestHeaders = requestHeadersJson && String(requestHeadersJson).trim() ? JSON.parse(requestHeadersJson) : {};
  let runtimePath = String(path || '').trim();
  if (!runtimePath) {
    runtimePath = '/mcp';
  }
  if (runtimePath.charAt(0) !== '/') {
    runtimePath = '/' + runtimePath;
  }
  if (runtimePath === '/agent/mcp' || runtimePath === '/agent/mcp/manifest') {
    return agentfaceMcpApi.handleRequest(method, runtimePath, bodyText || '', requestHeaders || {});
  }
  if (runtimePath === '/manage-boards') {
    return managedBoardsApi.handleRequest(method, bodyText);
  }
  if (runtimePath === '/mcp-extras') {
    return mcpExtrasApi.handleHttpTool(method, bodyText);
  }
  let routedBoardId = activeBoardId;
  const apiBoardsMatch = runtimePath.match(/^\/api\/boards\/([^/]+)\//);
  if (apiBoardsMatch) {
    routedBoardId = decodeURIComponent(apiBoardsMatch[1]);
  } else if (runtimePath.indexOf('/api/') !== 0) {
    runtimePath = '/api/boards/' + encodeURIComponent(activeBoardId) + runtimePath;
    routedBoardId = activeBoardId;
  }

  const runtime = getWinuiRuntime(routedBoardId);
  const parsedUrl = createParsedUrl(runtimePath);
  const request = createSyntheticRequest(
    String(method || 'GET').toUpperCase(),
    runtimePath,
    bodyText || '',
    { 'content-type': 'application/json' }
  );
  const response = createSyntheticResponse();

  await runtime.handleRuntimeApi(request, response.res, parsedUrl);
  if (typeof runtime.__drainProcessAccumulatedLane === 'function') {
    await runtime.__drainProcessAccumulatedLane();
  }

  return JSON.stringify({
    statusCode: response.statusCode(),
    headers: response.headers(),
    body: response.body(),
  }, null, 2);
}

