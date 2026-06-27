import path from 'node:path';
import { pathToFileURL } from 'node:url';

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString('utf8');
}

function normalizeObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
}

function normalizeString(value, fallback = '') {
  return typeof value === 'string' ? value.trim() || fallback : fallback;
}

function optionalString(value) {
  const normalized = normalizeString(value);
  return normalized || undefined;
}

function resolveNsCodeRepoRoot(payload) {
  const explicitRoot = normalizeString(payload.nsCodeRepoRoot);
  if (explicitRoot) {
    return explicitRoot;
  }

  const repoRoot = normalizeString(payload.repoRoot);
  return path.join(repoRoot, 'demo-boards-ns-code');
}

async function runTask(payload) {
  const nsCodeRepoRoot = resolveNsCodeRepoRoot(payload);
  const modulePath = path.join(nsCodeRepoRoot, 'demo-board', 'server', 'board-worker', 'task-executor.js');
  const request = normalizeObject(payload.request);
  const mod = await import(pathToFileURL(modulePath).href);
  await mod.executeTaskExecutorRequest(request);
  return { dispatched: true };
}

async function runChat(payload) {
  const nsCodeRepoRoot = resolveNsCodeRepoRoot(payload);
  const boardId = normalizeString(payload.boardId, 'winui-board');
  const modulePath = path.join(nsCodeRepoRoot, 'demo-board', 'server', 'hosted-board-runtime', 'host-shared', 'chat-agent-handler', 'execute-chat-agent-request.js');
  const mod = await import(pathToFileURL(modulePath).href);
  const boardRecord = normalizeObject(payload.boardRecord);
  const request = normalizeObject(payload.request);
  const serverUrl = normalizeString(payload.serverUrl);
  const mcpServerUrl = normalizeString(payload.mcpServerUrl, `${serverUrl}/agent/mcp`);
  const chatAgentHandlerNeeds = {
    boardId,
    serverUrl,
    notifyUrl: '',
    mcpServerUrl,
    agentFaceMcp: normalizeString(payload.agentFaceMcp, '/agent/mcp'),
    apiBasePath: `/api/boards/${encodeURIComponent(boardId)}`,
    ...(optionalString(boardRecord.aiWorkspaceRoot) ? { aiWorkspaceRoot: boardRecord.aiWorkspaceRoot.trim() } : {}),
    ...(optionalString(boardRecord.ai) ? { chatAssistant: boardRecord.ai.trim() } : {}),
  };
  const boardRuntimeNeeds = {
    chatAgentHandlerNeeds,
    taskExecutorExtra: {
      boardId,
      ...(optionalString(boardRecord.aiWorkspaceRoot) ? { aiWorkspaceRoot: boardRecord.aiWorkspaceRoot.trim() } : {}),
    },
  };
  await mod.executeChatAgentRequest(request, boardId, boardRuntimeNeeds);
  return { dispatched: true };
}

async function main() {
  const raw = await readStdin();
  const payload = normalizeObject(raw ? JSON.parse(raw) : {});
  const mode = normalizeString(payload.mode);
  if (mode === 'task') {
    process.stdout.write(JSON.stringify(await runTask(payload)));
    return;
  }
  if (mode === 'chat') {
    process.stdout.write(JSON.stringify(await runChat(payload)));
    return;
  }
  throw new Error(`Unsupported host invocation mode: ${mode || '<empty>'}`);
}

main().catch((error) => {
  const message = error instanceof Error && error.message ? error.message : String(error);
  process.stdout.write(JSON.stringify({ dispatched: false, error: message }));
  process.exit(1);
});