import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString('utf8');
}

function normalizeString(value, fallback = '') {
  return typeof value === 'string' ? value.trim() || fallback : fallback;
}

function normalizeObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
}

function cloneJsonValue(value) {
  return value == null ? value : JSON.parse(JSON.stringify(value));
}

function normalizeBoardRecordForSync(value) {
  const candidate = normalizeObject(value);
  const source = candidate.resolvedBoardConfig && typeof candidate.resolvedBoardConfig === 'object' && !Array.isArray(candidate.resolvedBoardConfig)
    ? normalizeObject(candidate.resolvedBoardConfig)
    : candidate;

  const record = {};
  for (const field of ['label', 'ai', 'aiWorkspaceTemplate', 'cardsTemplate', 'refsTemplate', 'uiTemplate']) {
    const normalized = normalizeString(source[field]);
    if (normalized) {
      record[field] = normalized;
    }
  }

  for (const field of ['metadata', 'chat', 'queueWakeup']) {
    const normalized = normalizeObject(source[field]);
    if (Object.keys(normalized).length > 0) {
      record[field] = cloneJsonValue(normalized);
    }
  }

  const refsTemplate = normalizeString(record.refsTemplate);
  const refs = normalizeObject(source.refs);
  if (!refsTemplate && Object.keys(refs).length > 0) {
    record.refs = cloneJsonValue(refs);
  }

  const uiTemplate = normalizeString(record.uiTemplate);
  const ui = normalizeObject(source.ui);
  if (!uiTemplate && Object.keys(ui).length > 0) {
    record.ui = cloneJsonValue(ui);
  }

  return record;
}

async function importLoadConfig(payload) {
  const loaderPath = normalizeString(payload.localFsConfigLoaderPath);
  if (!loaderPath) {
    throw new Error('localFsConfigLoaderPath is required');
  }
  return import(pathToFileURL(loaderPath).href);
}

async function loadHostConfig(payload) {
  const configPath = normalizeString(payload.hostConfigPath);
  if (!configPath) {
    throw new Error('hostConfigPath is required');
  }
  const mod = await importLoadConfig(payload);
  return mod.loadLocalFsHostConfig(configPath, ['--config', configPath], 'winui-host-config');
}

function readAssistantNames(assistantRegistryPath) {
  if (!assistantRegistryPath || !fs.existsSync(assistantRegistryPath)) {
    return [];
  }
  const parsed = JSON.parse(fs.readFileSync(assistantRegistryPath, 'utf8'));
  return Object.keys(normalizeObject(parsed)).sort((left, right) => left.localeCompare(right));
}

async function describeHostConfig(payload) {
  const hostConfig = await loadHostConfig(payload);
  return {
    ok: true,
    assistantNames: readAssistantNames(normalizeString(payload.assistantRegistryPath)),
    aiWorkspaceTemplateNames: Object.keys(normalizeObject(hostConfig.aiWorkspaceTemplates)).sort((left, right) => left.localeCompare(right)),
    uiTemplateNames: Object.keys(normalizeObject(hostConfig.uiTemplates)).sort((left, right) => left.localeCompare(right)),
    refsTemplateNames: Object.keys(normalizeObject(hostConfig.refsTemplates)).sort((left, right) => left.localeCompare(right)),
    hostConfigPath: normalizeString(hostConfig.configPath, normalizeString(payload.hostConfigPath)),
    templatesConfigPath: normalizeString(payload.templatesConfigPath),
    setupSingleAiWorkspaceScriptPath: normalizeString(payload.setupSingleAiWorkspaceScriptPath),
    sampleTemplateCatalogDir: normalizeString(hostConfig.sampleTemplateCatalog?.dir),
    runtimeBoardsIndexRef: JSON.stringify(hostConfig.boardsIndexRef || {}),
    runtimeBoardsLayoutRef: JSON.stringify(hostConfig.boardsLayoutRef || {}),
    rawHostSummaryJson: JSON.stringify({
      configPath: hostConfig.configPath,
      boardRoot: hostConfig.boardRoot,
      storageAdapter: hostConfig.storageAdapter,
      mcpServerUrl: hostConfig.mcpServerUrl,
      agentFaceMcp: hostConfig.agentFaceMcp,
      apiBasePrefix: hostConfig.apiBasePrefix,
      sampleTemplateCatalog: hostConfig.sampleTemplateCatalog,
      runtimeBoardsRegistry: hostConfig.runtimeBoardsRegistry,
      refsTemplates: Object.keys(normalizeObject(hostConfig.refsTemplates)),
      aiWorkspaceTemplates: Object.keys(normalizeObject(hostConfig.aiWorkspaceTemplates)),
      uiTemplates: Object.keys(normalizeObject(hostConfig.uiTemplates)),
    }, null, 2),
  };
}

async function resolveBoardConfig(payload) {
  const boardId = normalizeString(payload.boardId);
  if (!boardId) {
    throw new Error('boardId is required');
  }
  const record = normalizeObject(payload.record);
  const hostConfig = await loadHostConfig(payload);
  const mod = await importLoadConfig(payload);
  const resolvedBoardConfig = mod.buildBoardConfig(boardId, record, {
    configDir: hostConfig.configDir,
    boardRoot: hostConfig.boardRoot,
    refsTemplates: hostConfig.refsTemplates,
    aiWorkspaceTemplates: hostConfig.aiWorkspaceTemplates,
    uiTemplates: hostConfig.uiTemplates,
  });
  return {
    ok: true,
    resolvedBoardConfig,
  };
}

async function validateCardSchema(payload) {
  const card = normalizeObject(payload.card);
  const loaderPath = normalizeString(payload.localFsConfigLoaderPath);
  if (!loaderPath) {
    throw new Error('localFsConfigLoaderPath is required to resolve the card validator');
  }
  // Resolve yaml-flow's adapter-free card validator (AJV schema + JSONata + provides
  // checks) from the host module tree that owns the localfs config loader.
  const requireFromLoader = createRequire(pathToFileURL(loaderPath).href);
  const cardValidationPath = requireFromLoader.resolve('yaml-flow/card-validation');
  const mod = await import(pathToFileURL(cardValidationPath).href);
  const result = mod.validateCardPreflight(card);
  return {
    ok: true,
    validation: {
      ok: result?.isValid === true,
      errors: Array.isArray(result?.issues) ? result.issues : [],
    },
  };
}

async function syncBoardRecord(payload) {
  const boardId = normalizeString(payload.boardId);
  if (!boardId) {
    throw new Error('boardId is required');
  }
  const record = normalizeBoardRecordForSync(payload.record);
  const hostConfig = await loadHostConfig(payload);
  const loaderPath = normalizeString(payload.localFsConfigLoaderPath);
  const dynamicBoardsModulePath = path.resolve(path.dirname(loaderPath), '..', 'boards-index', 'dynamic-boards.js');
  const dynamicBoardsModule = await import(pathToFileURL(dynamicBoardsModulePath).href);
  const lifecycle = dynamicBoardsModule.createDynamicBoards({ hostConfig, adapterServices: {} });

  if (await lifecycle.has(boardId)) {
    await lifecycle.saveRecord(boardId, record);
  } else {
    await lifecycle.provision(boardId, record);
  }

  return {
    ok: true,
  };
}

async function main() {
  const raw = await readStdin();
  const payload = normalizeObject(raw ? JSON.parse(raw) : {});
  const mode = normalizeString(payload.mode);
  let result;
  if (mode === 'describe-host-config') {
    result = await describeHostConfig(payload);
  } else if (mode === 'resolve-board-config') {
    result = await resolveBoardConfig(payload);
  } else if (mode === 'sync-board-record') {
    result = await syncBoardRecord(payload);
  } else if (mode === 'validate-card-schema') {
    result = await validateCardSchema(payload);
  } else {
    throw new Error(`Unsupported mode: ${mode || '<empty>'}`);
  }

  process.stdout.write(JSON.stringify(result));
}

main().catch((error) => {
  const message = error instanceof Error && error.message ? error.message : String(error);
  process.stdout.write(JSON.stringify({ ok: false, error: message }));
  process.exit(1);
});