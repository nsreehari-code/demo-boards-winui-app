import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

import { resolveDotnet } from './resolve-dotnet.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const nsCodeRoot = path.resolve(repoRoot, '..', 'demo-boards-ns-code');
const myHttpTestPath = path.join(nsCodeRoot, 'demo-board', 'test', 'my-http-test.js');

const boardId = process.env.WINUI_MYHTTP_BOARD_ID || 'live-test-backend';
const port = Number.parseInt(process.env.WINUI_MYHTTP_PORT || '7799', 10);
const runTests = process.env.WINUI_MYHTTP_TESTS || 'MB1';
const extraArgs = process.argv.slice(2);

function waitForReady(child, timeoutMs = 45000) {
  return new Promise((resolve, reject) => {
    let stdout = '';
    let stderr = '';
    let settled = false;

    const onStdout = (chunk) => {
      const text = String(chunk);
      process.stdout.write(text);
      stdout += text;
      const readyMatch = stdout.match(/\[serve\] READY endpoint=([^\s]+) boardId=([^\s]+)/);
      if (readyMatch) {
        settled = true;
        cleanup();
        resolve({ endpoint: readyMatch[1], boardId: readyMatch[2] });
      }
    };

    const onStderr = (chunk) => {
      const text = String(chunk);
      process.stderr.write(text);
      stderr += text;
    };

    const onClose = (code) => {
      if (settled) return;
      settled = true;
      cleanup();
      reject(new Error(`RuntimeHost serve harness exited before ready (code ${code ?? -1}).\n${stderr}`));
    };

    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      cleanup();
      reject(new Error(`Timed out waiting for serve harness readiness after ${timeoutMs}ms.`));
    }, timeoutMs);

    const cleanup = () => {
      clearTimeout(timer);
      child.stdout.off('data', onStdout);
      child.stderr.off('data', onStderr);
      child.off('close', onClose);
    };

    child.stdout.on('data', onStdout);
    child.stderr.on('data', onStderr);
    child.on('close', onClose);
  });
}

function runMyHttpTest(portToUse) {
  return new Promise((resolve, reject) => {
    const child = spawn(
      'node',
      [myHttpTestPath, '--run-tests', runTests, '--port', String(portToUse), '--board-id', boardId, ...extraArgs],
      {
        cwd: nsCodeRoot,
        stdio: 'inherit',
      }
    );

    child.on('error', reject);
    child.on('close', (code) => resolve(code ?? 1));
  });
}

function stopServeHarness(child) {
  return new Promise((resolve) => {
    let done = false;

    const finish = () => {
      if (done) return;
      done = true;
      resolve();
    };

    child.once('close', finish);

    if (!child.killed && child.stdin && !child.stdin.destroyed) {
      child.stdin.write('exit\n');
      child.stdin.end();
    }

    setTimeout(() => {
      if (!done) {
        child.kill('SIGTERM');
      }
    }, 3000);

    setTimeout(() => {
      if (!done) {
        child.kill('SIGKILL');
      }
    }, 7000);
  });
}

async function main() {
  const dotnet = resolveDotnet();
  const serveArgs = [
    'run',
    '--project',
    path.join('app', 'engines', 'RuntimeHostT0Harness', 'RuntimeHostT0Harness.csproj'),
    '--',
    '--serve',
    '--board-id',
    boardId,
    '--port',
    String(port),
  ];

  const serveChild = spawn(dotnet, serveArgs, {
    cwd: repoRoot,
    stdio: ['pipe', 'pipe', 'pipe'],
  });

  serveChild.on('error', (error) => {
    console.error(error);
  });

  try {
    console.log(`[runner] starting RuntimeHost serve harness on http://127.0.0.1:${port} for board '${boardId}'`);
    const ready = await waitForReady(serveChild);
    const readyPort = Number.parseInt(new URL(ready.endpoint).port || String(port), 10) || port;
    console.log(`[runner] harness ready at ${ready.endpoint} (board ${ready.boardId})`);
    console.log(`[runner] executing my-http-test.js with --run-tests ${runTests}`);
    const code = await runMyHttpTest(readyPort);
    await stopServeHarness(serveChild);
    process.exit(code);
  } catch (error) {
    console.error('[runner] failed:', error instanceof Error ? error.message : String(error));
    await stopServeHarness(serveChild);
    process.exit(1);
  }
}

main();
